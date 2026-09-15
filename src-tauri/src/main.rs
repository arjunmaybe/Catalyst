#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::collections::VecDeque;
use std::path::{Path, PathBuf};
use std::process::Stdio;

use tauri::ipc::Channel;
use tauri::menu::{MenuBuilder, MenuItemBuilder};
use tauri::tray::TrayIconBuilder;
use tauri::{AppHandle, Emitter, Manager, PhysicalPosition, Position, WindowEvent};
use tokio::io::{AsyncBufReadExt, BufReader};

// ---------------------------------------------------------------------------
// Paths & settings.
// ---------------------------------------------------------------------------

const WIDGET_SIZE: i32 = 260;
const CORNER_MARGIN: i32 = 24;

/// `<name>_catalyst<ext>` next to the original file.
fn output_path_for(source: &str, target_ext: &str) -> PathBuf {
    let p = Path::new(source);
    let parent = p.parent().unwrap_or_else(|| Path::new(""));
    let stem = p.file_stem().and_then(|s| s.to_str()).unwrap_or("output");
    parent.join(format!("{stem}_catalyst{target_ext}"))
}

fn settings_path(app: &AppHandle) -> Option<PathBuf> {
    app.path()
        .app_data_dir()
        .ok()
        .map(|dir| dir.join("settings.json"))
}

#[derive(serde::Serialize, serde::Deserialize)]
struct WindowPos {
    x: i32,
    y: i32,
}

fn save_window_position(app: &AppHandle) {
    let Some(win) = app.get_webview_window("main") else {
        return;
    };
    let Ok(pos) = win.outer_position() else {
        return;
    };
    let Some(path) = settings_path(app) else {
        return;
    };
    if let Some(dir) = path.parent() {
        let _ = std::fs::create_dir_all(dir);
    }
    if let Ok(json) = serde_json::to_string(&WindowPos { x: pos.x, y: pos.y }) {
        let _ = std::fs::write(path, json);
    }
}

fn restore_window_position(app: &AppHandle, win: &tauri::WebviewWindow) {
    if let Some(path) = settings_path(app) {
        if let Ok(json) = std::fs::read_to_string(path) {
            if let Ok(pos) = serde_json::from_str::<WindowPos>(&json) {
                let _ = win.set_position(Position::Physical(PhysicalPosition {
                    x: pos.x,
                    y: pos.y,
                }));
                return;
            }
        }
    }
    // First run (or corrupt settings): park in the bottom-right corner.
    if let Ok(Some(mon)) = win.current_monitor() {
        let size = mon.size();
        let origin = mon.position();
        let x = origin.x + size.width as i32 - WIDGET_SIZE - CORNER_MARGIN;
        let y = origin.y + size.height as i32 - WIDGET_SIZE - CORNER_MARGIN;
        let _ = win.set_position(Position::Physical(PhysicalPosition { x, y }));
    }
}

// ---------------------------------------------------------------------------
// Image conversion (Rust `image` crate).
// ---------------------------------------------------------------------------

#[tauri::command]
fn convert_image(path: String, target_ext: String) -> Result<String, String> {
    let img = image::open(&path).map_err(|e| format!("open failed: {e}"))?;
    let out = output_path_for(&path, &target_ext);
    let format = match target_ext.to_lowercase().as_str() {
        ".jpg" | ".jpeg" => image::ImageFormat::Jpeg,
        ".png" => image::ImageFormat::Png,
        ".bmp" => image::ImageFormat::Bmp,
        ".gif" => image::ImageFormat::Gif,
        other => return Err(format!("unsupported image target: {other}")),
    };
    img.save_with_format(&out, format)
        .map_err(|e| format!("encode failed: {e}"))?;
    Ok(out.to_string_lossy().into_owned())
}

// ---------------------------------------------------------------------------
// Video/audio conversion (ffmpeg sidecar downloaded at build time).
// ---------------------------------------------------------------------------

/// Filename Tauri expects for the sidecar in `src-tauri/binaries/`.
fn dev_sidecar_filename() -> String {
    if cfg!(windows) {
        "ffmpeg-x86_64-pc-windows-msvc.exe".to_string()
    } else {
        "ffmpeg-x86_64-unknown-linux-gnu".to_string()
    }
}

fn resolve_ffmpeg() -> Result<PathBuf, String> {
    let exe_name = if cfg!(windows) {
        "ffmpeg.exe"
    } else {
        "ffmpeg"
    };

    // 1. Next to the app binary (Tauri stages `externalBin` there).
    if let Ok(exe) = std::env::current_exe() {
        if let Some(dir) = exe.parent() {
            let candidate = dir.join(exe_name);
            if candidate.is_file() {
                return Ok(candidate);
            }
        }
    }

    // 2. Dev layout straight out of `src-tauri/binaries/`.
    let dev = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("binaries")
        .join(dev_sidecar_filename());
    if dev.is_file() {
        return Ok(dev);
    }

    // 3. PATH fallback ("ffmpeg" must resolve at spawn time).
    Ok(PathBuf::from(exe_name))
}

fn friendly_spawn_error(e: std::io::Error) -> String {
    if e.kind() == std::io::ErrorKind::NotFound {
        "ffmpeg missing — rebuild once with internet (auto-download), or place ffmpeg on PATH."
            .to_string()
    } else {
        format!("ffmpeg failed to start: {e}")
    }
}

/// Parse `Duration: 00:01:23.45` from `ffmpeg -i` stderr output.
fn parse_duration_secs(text: &str) -> Option<f64> {
    let idx = text.find("Duration:")?;
    let rest = text[idx + "Duration:".len()..].trim_start();
    let mut parts = rest.split(':');
    let h: f64 = parts.next()?.trim().parse().ok()?;
    let m: f64 = parts.next()?.trim().parse().ok()?;
    let sec_field = parts.next()?;
    let sec_str = sec_field.split([',', ' ']).next()?.trim();
    let s: f64 = sec_str.parse().ok()?;
    Some(h * 3600.0 + m * 60.0 + s)
}

/// Parse a `-progress pipe:1` line (`out_time_ms=` / `out_time_us=` are µs).
fn progress_secs_from_line(line: &str) -> Option<f64> {
    if let Some(v) = line.strip_prefix("out_time_ms=") {
        return v.trim().parse::<i64>().ok().map(|ms| ms as f64 / 1_000_000.0);
    }
    if let Some(v) = line.strip_prefix("out_time_us=") {
        return v.trim().parse::<i64>().ok().map(|us| us as f64 / 1_000_000.0);
    }
    None
}

/// Fallback progress parse from stderr status lines (`time=00:00:12.34`).
fn stderr_time_secs(line: &str) -> Option<f64> {
    let idx = line.find("time=")?;
    let rest = line[idx + "time=".len()..].trim_start();
    let field = rest.split_whitespace().next()?;
    let mut parts = field.split(':');
    let h: f64 = parts.next()?.parse().ok()?;
    let m: f64 = parts.next()?.parse().ok()?;
    let s: f64 = parts.next()?.parse().ok()?;
    Some(h * 3600.0 + m * 60.0 + s)
}

fn trim_status(message: &str, max: usize) -> String {
    let flat = message.trim().replace(['\r', '\n'], " ");
    if flat.len() <= max {
        flat
    } else {
        flat.chars().take(max).collect()
    }
}

async fn probe_duration_secs(ffmpeg: &Path, source: &str) -> f64 {
    let out = tokio::process::Command::new(ffmpeg)
        .args(["-hide_banner", "-i", source])
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::piped())
        .output()
        .await;
    match out {
        Ok(o) => parse_duration_secs(&String::from_utf8_lossy(&o.stderr)).unwrap_or(0.0),
        Err(_) => 0.0,
    }
}

#[tauri::command]
async fn convert_media(
    path: String,
    target_ext: String,
    on_progress: Channel<f64>,
) -> Result<String, String> {
    let ffmpeg = resolve_ffmpeg()?;
    let out = output_path_for(&path, &target_ext);
    let total = probe_duration_secs(&ffmpeg, &path).await;

    let mut child = tokio::process::Command::new(&ffmpeg)
        .args([
            "-y",
            "-hide_banner",
            "-nostats",
            "-progress",
            "pipe:1",
            "-i",
            &path,
        ])
        .arg(&out)
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .map_err(friendly_spawn_error)?;

    // -progress lines on stdout.
    let stdout_task = child.stdout.take().map(|s| {
        let ch = on_progress.clone();
        tokio::spawn(async move {
            let mut lines = BufReader::new(s).lines();
            while let Ok(Some(line)) = lines.next_line().await {
                if total > 0.0 {
                    if let Some(sec) = progress_secs_from_line(&line) {
                        let _ = ch.send((sec / total).clamp(0.0, 1.0));
                    }
                }
            }
        })
    });

    // stderr: fallback progress + error tail for failure messages.
    let stderr_task = child.stderr.take().map(|s| {
        let ch = on_progress.clone();
        tokio::spawn(async move {
            let mut tail: VecDeque<String> = VecDeque::with_capacity(20);
            let mut lines = BufReader::new(s).lines();
            while let Ok(Some(line)) = lines.next_line().await {
                if total > 0.0 {
                    if let Some(sec) = stderr_time_secs(&line) {
                        let _ = ch.send((sec / total).clamp(0.0, 1.0));
                    }
                }
                if tail.len() == 20 {
                    tail.pop_front();
                }
                tail.push_back(line);
            }
            tail.into_iter().collect::<Vec<_>>().join(" ")
        })
    });

    let status = child
        .wait()
        .await
        .map_err(|e| format!("ffmpeg wait failed: {e}"))?;

    if let Some(t) = stdout_task {
        let _ = t.await;
    }
    let tail = match stderr_task {
        Some(t) => t.await.unwrap_or_default(),
        None => String::new(),
    };

    if !status.success() {
        let _ = std::fs::remove_file(&out);
        return Err(if tail.trim().is_empty() {
            "ffmpeg failed.".to_string()
        } else {
            format!("ffmpeg failed: {}", trim_status(&tail, 80))
        });
    }

    let _ = on_progress.send(1.0);
    Ok(out.to_string_lossy().into_owned())
}

// ---------------------------------------------------------------------------
// App setup: position restore, tray menu, close persistence.
// ---------------------------------------------------------------------------

fn build_tray(app: &AppHandle) -> tauri::Result<()> {
    let settings = MenuItemBuilder::with_id("settings", "Settings").build(app)?;
    let quit = MenuItemBuilder::with_id("quit", "Quit").build(app)?;
    let menu = MenuBuilder::new(app).items(&[&settings, &quit]).build()?;

    let builder = TrayIconBuilder::with_id("catalyst-tray")
        .tooltip("Catalyst")
        .menu(&menu)
        .on_menu_event(|app, event| match event.id().as_ref() {
            "settings" => {
                // Placeholder: nudge the widget instead of opening anything.
                if let Some(win) = app.get_webview_window("main") {
                    let _ = win.emit("tray-settings", ());
                }
            }
            "quit" => {
                save_window_position(app);
                app.exit(0);
            }
            _ => {}
        });

    let builder = match app.default_window_icon() {
        Some(icon) => builder.icon(icon.clone()),
        None => builder,
    };
    builder.build(app)?;
    Ok(())
}

fn main() {
    tauri::Builder::default()
        .setup(|app| {
            let win = app
                .get_webview_window("main")
                .expect("main window missing from tauri.conf.json");

            restore_window_position(app.handle(), &win);
            let _ = win.show();

            build_tray(app.handle())?;

            let handle = app.handle().clone();
            win.on_window_event(move |event| {
                if matches!(event, WindowEvent::CloseRequested { .. }) {
                    save_window_position(&handle);
                }
            });
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![convert_image, convert_media])
        .run(tauri::generate_context!())
        .expect("failed to run Catalyst");
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn output_path_appends_catalyst_suffix() {
        let out = output_path_for("C:\\pics\\photo.png", ".jpg");
        assert_eq!(out, PathBuf::from("C:\\pics\\photo_catalyst.jpg"));
    }

    #[test]
    fn parses_ffmpeg_duration_line() {
        let text = "  Duration: 00:01:23.45, start: 0.000000, bitrate: 1000 kb/s";
        assert!((parse_duration_secs(text).unwrap() - 83.45).abs() < 1e-6);
        assert!(parse_duration_secs("no duration here").is_none());
    }

    #[test]
    fn parses_progress_lines() {
        assert!((progress_secs_from_line("out_time_ms=8300000").unwrap() - 8.3).abs() < 1e-6);
        assert!((progress_secs_from_line("out_time_us=8300000").unwrap() - 8.3).abs() < 1e-6);
        assert!(progress_secs_from_line("progress=continue").is_none());
    }

    #[test]
    fn parses_stderr_time_fallback() {
        let line = "frame=  100 fps=0.0 q=28.0 size=     256KiB time=00:00:08.30 bitrate= 256.0kbits/s";
        assert!((stderr_time_secs(line).unwrap() - 8.3).abs() < 1e-6);
    }

    #[test]
    fn convert_image_roundtrip() {
        let dir = std::env::temp_dir().join(format!("catalyst-test-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let src = dir.join("sample.png");
        let img = image::RgbImage::from_fn(8, 8, |x, y| {
            image::Rgb([(x * 32) as u8, (y * 32) as u8, 128])
        });
        img.save(&src).unwrap();

        for target in [".jpg", ".bmp", ".gif", ".png"] {
            let out = convert_image(
                src.to_string_lossy().into_owned(),
                target.to_string(),
            )
            .expect("conversion should succeed");
            assert!(out.ends_with(&format!("_catalyst{target}")));
            assert!(std::fs::metadata(&out).unwrap().len() > 0);
        }

        assert!(convert_image(src.to_string_lossy().into_owned(), ".mp4".to_string()).is_err());
        let _ = std::fs::remove_dir_all(&dir);
    }
}
