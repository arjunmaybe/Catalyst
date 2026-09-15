use std::path::PathBuf;

/// Ensures the ffmpeg sidecar binary exists in `src-tauri/binaries/` before
/// the Tauri bundler looks for it (`externalBin: ["binaries/ffmpeg"]`).
/// Downloads the gyan.dev essentials build on first use only; the binaries
/// directory is gitignored. Set `CATALYST_SKIP_FFMPEG=1` for offline builds
/// (ffmpeg then resolves via PATH at runtime, or media conversion fails
/// with a friendly message).
fn main() {
    // NOTE: the ffmpeg sidecar must be downloaded BEFORE tauri_build runs,
    // because tauri_build validates `externalBin` paths up front.
    ensure_ffmpeg_sidecar();

    tauri_build::try_build(tauri_build::Attributes::default())
        .expect("failed to run tauri-build");
}

fn ensure_ffmpeg_sidecar() {
    let manifest_dir = PathBuf::from(std::env::var("CARGO_MANIFEST_DIR").unwrap());
    let target = std::env::var("TARGET").unwrap_or_else(|_| "unknown".to_string());
    let exe_name = if target.contains("windows") {
        format!("ffmpeg-{target}.exe")
    } else {
        format!("ffmpeg-{target}")
    };
    let dest = manifest_dir.join("binaries").join(&exe_name);

    println!("cargo:rerun-if-changed=build.rs");
    println!("cargo:rerun-if-changed=../tools/fetch-ffmpeg.ps1");

    if dest.is_file() {
        println!("cargo:warning=ffmpeg sidecar already present, skipping download.");
        return;
    }
    if std::env::var("CATALYST_SKIP_FFMPEG").as_deref() == Ok("1") {
        println!("cargo:warning=CATALYST_SKIP_FFMPEG=1, skipping ffmpeg download.");
        return;
    }

    println!("cargo:warning=downloading ffmpeg sidecar (first build only)...");
    let script = manifest_dir.join("../tools/fetch-ffmpeg.ps1");
    let status = std::process::Command::new("powershell")
        .args(["-NoProfile", "-ExecutionPolicy", "Bypass", "-File"])
        .arg(&script)
        .args([
            "-ZipUrl",
            "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip",
            "-DestinationExe",
        ])
        .arg(&dest)
        .status()
        .expect("failed to launch powershell for ffmpeg download");

    if !status.success() {
        panic!("ffmpeg sidecar download failed; retry with internet or set CATALYST_SKIP_FFMPEG=1");
    }
    assert!(
        dest.is_file(),
        "fetch script reported success but {} is missing",
        dest.display()
    );
}
