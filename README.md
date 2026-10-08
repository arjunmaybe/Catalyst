# Catalyst

A floating file converter for Windows. A small disc sits on your desktop:
drag a file onto it, release over a format in the ring, and the converted
copy lands next to the original. Everything runs locally; nothing uploads.

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
- Internet on first build (pinned ffmpeg sidecar auto-download, cached afterwards; see `tools/ffmpeg-manifest.json`)

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

Verify the cached sidecar against the pin without downloading:

```powershell
npm run ffmpeg:verify
```

Release builds, versioning, signing, and the beta checklist live in
`docs/RELEASING.md`, `docs/SIGNING.md`, and `docs/BETA-CHECKLIST.md`.

## Tests

```powershell
npx tsc --noEmit
cargo test --manifest-path src-tauri/Cargo.toml
```

The Rust tests cover the output-naming rule, ffmpeg duration/progress
parsing, and an image roundtrip through `convert_image` (PNG → JPG, BMP,
GIF, PNG, plus a rejected non-image target).

## Website preview

`landing/` is a standalone static site with no build step. Preview it with
any static server:

```powershell
npx serve landing
```

`landing/demo.mp4` is a real screen recording of the widget on Windows, not
a mockup. The page also embeds a working model of the format ring; its
source/target data mirrors the matrix in `src/main.ts`.

## Where things live

- `src/main.ts` — widget UI, drag-drop, and the format matrix
  (`IMAGE_TARGETS` / `VIDEO_TARGETS` / `AUDIO_TARGETS`). The ring only
  offers targets from the dropped file's category, excluding its own
  extension; the backend re-validates per command.
- `src-tauri/src/main.rs` — `convert_image` (`image` crate), `convert_media`
  (ffmpeg sidecar with progress channel), tray menu, window-position
  persistence. Unit tests at the bottom of the file.
- `src-tauri/build.rs` + `tools/fetch-ffmpeg.ps1` + `tools/ffmpeg-manifest.json` — pinned, checksum-verified ffmpeg sidecar download (gyan.dev essentials build into gitignored `src-tauri/binaries/`); skipped with `CATALYST_SKIP_FFMPEG=1`. Verify with `npm run ffmpeg:verify`.
- `landing/` — static website (`index.html` + `styles.css` + `demo.mp4`).
- `wpf-prototype/` — archived WPF/.NET prototype, kept as a behavior
  reference only; not part of the build.

## Support

Support and feature requests live on
[GitHub Issues](https://github.com/arjunmaybe/Catalyst/issues). For conversion
failures, include the message shown on the widget plus the full technical
error from the nucleus hover tooltip — that is what the issue template asks
for.

## Licensing

Catalyst source is publicly viewable, but Catalyst is proprietary
software — it is not licensed under an open-source license.

- `LICENSE` — proprietary source license (copyright Arjun Sasi).
- `EULA.md` — draft end-user agreement for official Windows releases,
  which may be sold. No commercial distribution has launched yet.
- `THIRD-PARTY-NOTICES.md` — licenses for bundled and build-time
  third-party software (notably the GPLv3-licensed FFmpeg sidecar), which
  remain under their own licenses.
