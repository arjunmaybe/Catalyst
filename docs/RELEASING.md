# Releasing Catalyst (Windows beta)

This is the single source of truth for version numbers, tags, and the
reproducible Windows release build. It assumes a clean Windows 10/11
machine with Node 20+, Rust stable, and internet on first build.

No payments, accounts, DRM, activation, subscriptions, analytics, or new
product features are part of the beta. Do not add them here.

## 1. Version locations (keep in sync)

The app version lives in exactly three places. They must be identical
before tagging. Check with:

```powershell
npm run check:versions
```

| File | Field |
| --- | --- |
| `package.json` | `version` |
| `src-tauri/tauri.conf.json` | `version` |
| `src-tauri/Cargo.toml` | `[package] version` |

Current beta line: `0.1.0`. The first public beta tag is `v0.1.0-beta.1`
(the leading `v` is the git tag convention; the app version stays
`0.1.0` semver).

`tools/ffmpeg-manifest.json` is versioned separately (the FFmpeg sidecar
pin). Record its `pinnedVersion` in the release notes; it is not the app
version. Current pin: FFmpeg `9.0.2` essentials (see
`THIRD-PARTY-NOTICES.md`).

To cut a new beta:

1. Bump all three app versions to the same semver (e.g. `0.1.0` stays
   `0.1.0` for `beta.1`, `beta.2`, …; move to `0.1.1`/`0.2.0` only for a
   real release).
2. Run `npm run check:versions`.
3. Commit the bump, then tag (Section 4).

## 2. FFmpeg pin update (only when the sidecar changes)

Never float `ffmpeg-release-essentials.zip`. To move the pin (e.g.
9.0.2 → 9.0.3):

1. Open `https://www.gyan.dev/ffmpeg/builds/` and note the new release
   version, date, `.ver`, `.sha256`, and source commit.
2. Update `tools/ffmpeg-manifest.json`: `pinnedVersion`,
   `expectedVersionString`, `zipSha256`, `expectedVer`, `releaseDate`,
   `sourceCommit`.
3. Update the FFmpeg section of `THIRD-PARTY-NOTICES.md` in the same
   commit (version, SHA256, commit link).
4. Delete the stale cache (`src-tauri/binaries/ffmpeg-*.exe*`) and run
   `npm run ffmpeg:fetch`, then `npm run ffmpeg:verify`.
5. Record the new `ffmpeg -version` first line + exe SHA256 in the
   release notes.

Do not switch to `git-master` nightlies or to the `full` variant for a
release without a fresh license/configuration audit (the full variant
pulls extra libraries and a larger attack surface).

## 3. Reproducible local Windows build

Clean, deterministic-input build (lockfiles + pinned FFmpeg):

```powershell
npm ci
npm run check:versions
npx tsc --noEmit
cargo test --manifest-path src-tauri/Cargo.toml
npm run ffmpeg:verify
npx tauri build
powershell -NoProfile -ExecutionPolicy Bypass -File tools/hash-artifacts.ps1
```

Notes:

- `npm ci` (not `npm install`) respects `package-lock.json`.
- `src-tauri/Cargo.lock` is committed; do not use `--locked` overrides
  outside this flow.
- The first build downloads the pinned FFmpeg zip (≈109 MB) and verifies
  its SHA256; subsequent builds reuse the cached
  `src-tauri/binaries/ffmpeg-x86_64-pc-windows-msvc.exe` when its
  `-version` matches the pin. Force a re-fetch with `-Force` (see
  `tools/fetch-ffmpeg.ps1`) after deleting the cached exe.
- Offline fallback (dev only, never for a release):
  `$env:CATALYST_SKIP_FFMPEG = "1"; npx tauri build`.
- Output: `src-tauri/target/release/bundle/nsis/` holds
  `Catalyst_0.1.0_x64-setup.exe` (name varies by version/arch) plus
  `.sha256` files written by `tools/hash-artifacts.ps1`.
- The installer shows `EULA.md` as its license page
  (`bundle.licenseFile`) and ships `THIRD-PARTY-NOTICES.md`, `EULA.md`,
  and `licenses/GPL-3.0-or-later.txt` under its resources
  (`bundle.resources`).

Toolchain used to validate the beta pipeline (record any drift in the
release notes): Node `22.18.0`, npm `10.9.3`, Rust `1.98.0`,
`@tauri-apps/cli` `2.11.4`, `tauri-action` `v1`, runner
`windows-latest`.

Determinism scope: same committed inputs (`package-lock.json`,
`Cargo.lock`, `ffmpeg-manifest.json` + verified zip, identical app
version, clean `target/` + `binaries/` state) produce the same installer
payload and identical SHA256 hashes for verification. Bit-identical
reproducibility across different toolchains/runners (Rust/NSIS
timestamps) is not claimed; checksums are for per-release verification,
not cross-machine byte equality.

## 4. Tagging and the first beta release (exact commands)

CI creates a **draft pre-release** on every `v*` tag; nothing publishes
until a maintainer reviews the draft. Do not push until
`docs/BETA-CHECKLIST.md` is green.

```powershell
git status --short
npm run release:verify
npx tsc --noEmit
cargo test --manifest-path src-tauri/Cargo.toml
git add -A
git status --short
git commit -m "chore(release): prepare Windows beta v0.1.0-beta.1"
git tag -a v0.1.0-beta.1 -m "Catalyst v0.1.0-beta.1 (Windows beta)"
git log --oneline -3
git show --stat --oneline v0.1.0-beta.1
```

Then, only when the checklist is complete:

```powershell
git push origin main
git push origin v0.1.0-beta.1
```

After the push:

1. Watch Actions → `release` → `publish-windows-beta` go green.
2. Open the draft release GitHub created (`Catalyst v0.1.0-beta.1`,
   pre-release). Download the `windows-beta-sha256` and `ffmpeg-audit`
   workflow artifacts; attach the `*.sha256` files to the draft.
3. Edit the draft body with the real artifact hashes
   (`tools/hash-artifacts.ps1` output format), the FFmpeg
   `ffmpeg -version` line, and the toolchain versions above.
4. Install-test the NSIS `.exe` on a clean Windows 10/11 machine with
   WebView2 (Section 5), then publish the draft manually.

Manual dispatch without a tag (`Actions → release → Run workflow`) builds
and uploads artifacts but does not create a versioned release; use tags
for the beta.

## 5. Install test (required before publishing the draft)

1. On a clean Windows 10/11 VM or second machine with WebView2, install
   the NSIS `.exe` from the draft.
2. Launch from Start Menu; the widget nucleus appears bottom-right.
3. Convert one image (PNG → JPG), one video (MP4 → MKV), one audio file
   (WAV → MP3); confirm `<name>_catalyst<ext>` lands next to the original.
4. Confirm the install dir contains the FFmpeg sidecar, and the resource
   dir contains `THIRD-PARTY-NOTICES.md` + `GPL-3.0-or-later.txt`.
5. Uninstall via Add/Remove Programs; confirm no widget/tray residue.

## 6. What CI does

- `.github/workflows/ci.yml` (push/PR to `main`, `windows-latest`):
  `npm ci`, version sync, `tsc --noEmit`, `cargo test`
  (`CATALYST_SKIP_FFMPEG=1`), FFmpeg manifest sanity check.
- `.github/workflows/release.yml` (tags `v*` + manual dispatch,
  `windows-latest`): same checks (offline Rust tests), optional signing
  import (skipped when secrets are absent), `tauri-apps/tauri-action@v1`
  (`releaseDraft: true`, `prerelease: true`), SHA256 hashing, checksum +
  FFmpeg audit artifacts.
