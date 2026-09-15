# Catalyst (Tauri v2 rewrite)

A floating always-on-top "nucleus" widget you drag files onto to convert them.
Vanilla TypeScript frontend (Vite) + Rust backend, packaged with Tauri v2.

## What it does

- Sits on screen as a small blue circle (the "nucleus", ~92px in a 260x260
  transparent always-on-top window, hidden from the taskbar).
- Drag a file over it: format nodes appear in a ring around it (valid targets
  for that file's category, excluding its current format). The node nearest
  the cursor highlights. Drop to convert to the highlighted format.
  - Image (`.jpg/.jpeg/.png/.bmp/.gif/.tiff`) → JPG, PNG, BMP, GIF
    (converted in Rust via the `image` crate)
  - Video (`.mp4/.mkv/.mov/.avi/.webm`) → MP4, MKV, MOV, WEBM
  - Audio (`.mp3/.wav/.m4a/.flac/.ogg`) → MP3, WAV, M4A, FLAC
    (video/audio shell out to an ffmpeg sidecar binary)
- Converted files save next to the original as `<name>_catalyst<ext>`.
- Multi-file drops convert only files matching the first file's category and
  report `done (N)` / `done (N), skipped (M)`.
- Drag the nucleus to reposition; position persists to the app data dir and
  restores on launch (bottom-right corner on first run).
- Tray icon menu: **Settings** (placeholder) and **Quit**.

## Known limitations

- Some JPEG variants (e.g. certain progressive JPEGs or unusual encodings)
  can't be decoded by the Rust `image` crate used for image conversion. When
  this happens the widget shows a friendly message ("unsupported JPEG
  variant" / "couldn't read this image format") while the full technical
  decoder error is logged to the console (and kept on the nucleus hover
  tooltip) for debugging. Other `.jpg` files convert normally; swapping
  decoders for this edge case is intentionally out of scope.

## Requirements

- Windows 10/11
- [Node.js 20+](https://nodejs.org/) and [Rust stable](https://rustup.rs/)
- Internet on first build (ffmpeg sidecar auto-download, cached afterwards)

## Running it

```powershell
npm install
npx tauri dev
```

Production bundle:

```powershell
npx tauri build
```

Offline build (skips the ffmpeg download; media conversion then needs
ffmpeg on PATH):

```powershell
$env:CATALYST_SKIP_FFMPEG = "1"
npx tauri build
```

## Layout

- `src/` — Vanilla TS frontend (widget UI, drag-drop, progress)
- `src-tauri/` — Rust backend (conversion commands, tray, position store)
- `tools/fetch-ffmpeg.ps1` — build-time ffmpeg sidecar download
- `wpf-prototype/` — archived WPF/.NET prototype, kept as a behavior
  reference only; not part of the build.
