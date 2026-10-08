use std::path::PathBuf;

/// Ensures the ffmpeg sidecar binary exists in `src-tauri/binaries/` before
/// the Tauri bundler looks for it (`externalBin: ["binaries/ffmpeg"]`).
/// Downloads the pinned gyan.dev essentials build from
/// `tools/ffmpeg-manifest.json` (checksum-verified) when the cached exe is
/// missing or stale; the binaries directory is gitignored.
///
/// Test/release split (see `main`): `CATALYST_SKIP_FFMPEG=1` is test-only.
/// It skips the download AND clears `bundle.externalBin` via the
/// `TAURI_CONFIG` JSON-merge hook that `tauri-build` (pinned 2.6.x) applies
/// over `tauri.conf.json`, so `cargo test` compiles without the ~109 MB
/// sidecar. Release builds (variable unset) always download/verify the real
/// pinned binary and keep full `tauri-build` resource validation — no fake
/// executable is ever created.
fn main() {
    println!("cargo:rerun-if-changed=build.rs");
    println!("cargo:rerun-if-changed=../tools/fetch-ffmpeg.ps1");
    println!("cargo:rerun-if-changed=../tools/ffmpeg-manifest.json");
    println!("cargo:rerun-if-env-changed=CATALYST_SKIP_FFMPEG");
    // NOTE: tauri-build itself emits `cargo:rerun-if-env-changed=TAURI_CONFIG`.

    if std::env::var("CATALYST_SKIP_FFMPEG").as_deref() == Ok("1") {
        // Test-only path: no download, no sidecar validation.
        // `tauri-build` 2.6 has no `Attributes::config_path`, but it merges
        // `$TAURI_CONFIG` (JSON Merge Patch, RFC 7396) over the file config,
        // so an empty `externalBin` list disables the copy/validate step
        // while ACLs, cfgs and resources still generate normally.
        if std::env::var_os("TAURI_CONFIG").is_some() {
            println!("cargo:warning=CATALYST_SKIP_FFMPEG=1 overrides existing TAURI_CONFIG to clear bundle.externalBin for tests.");
        }
        std::env::set_var("TAURI_CONFIG", r#"{"bundle":{"externalBin":[]}}"#);
        println!("cargo:warning=CATALYST_SKIP_FFMPEG=1, building without ffmpeg sidecar (tests only; release still requires the pinned binary).");
        tauri_build::try_build(tauri_build::Attributes::default())
            .expect("failed to run tauri-build");
        return;
    }

    // Release path: the ffmpeg sidecar must be downloaded BEFORE tauri_build
    // runs, because tauri_build validates `externalBin` paths up front.
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
    let manifest = manifest_dir.join("../tools/ffmpeg-manifest.json");

    // Release path only: the SKIP branch returns before reaching here.

    println!("cargo:warning=ensuring pinned ffmpeg sidecar (see tools/ffmpeg-manifest.json)...");
    let script = manifest_dir.join("../tools/fetch-ffmpeg.ps1");
    let status = std::process::Command::new("powershell")
        .args(["-NoProfile", "-ExecutionPolicy", "Bypass", "-File"])
        .arg(&script)
        .args(["-ManifestPath"])
        .arg(&manifest)
        .args(["-DestinationExe"])
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
