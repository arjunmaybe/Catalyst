# Catalyst first public Windows beta — release checklist

Copy this list into the GitHub draft release notes or a tracking issue and
check items off. Nothing publishes until every box is checked and the
draft is manually published.

Scope guard: website, widget, conversion logic, proprietary licensing, and
repository history are preserved. No payments, accounts, DRM, activation,
subscriptions, analytics, or new features in the beta.

## A. Legal and licensing (blocking for any distribution)

- [ ] `THIRD-PARTY-NOTICES.md` FFmpeg section names the exact beta pin
      (9.0.2 essentials, zip SHA256
      `60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba`,
      source commit `946fcce07b`).
- [ ] `licenses/GPL-3.0-or-later.txt` is vendored and ships via
      `bundle.resources` (with `THIRD-PARTY-NOTICES.md` + `EULA.md`).
- [ ] Draft GitHub release body repeats the FFmpeg version, zip SHA256,
      and the three source links (`ffmpeg.org/download.html`,
      `gyan.dev/ffmpeg/builds`, exact source commit).
- [ ] `EULA.md` still says **Draft** and leaves governing law blank —
      acknowledged: have counsel review it before any *paid* release.
      The free public beta may proceed with the draft, but do not sell
      it without that review.
- [ ] No LGPL/full-variant, nightly, or locally built FFmpeg slipped in
      (`npm run ffmpeg:verify` shows `9.0.2-essentials_build` +
      `--enable-gpl --enable-version3` + GPL `-L` notice).

## B. Versioning and source control

- [ ] `npm run check:versions` passes (`package.json`,
      `tauri.conf.json`, `Cargo.toml` identical).
- [ ] Tag is `v0.1.0-beta.1` annotated (`git show v0.1.0-beta.1`),
      pushed only after this list is otherwise green.
- [ ] `git status` clean; `Cargo.lock` + `package-lock.json` committed;
      no `src-tauri/binaries/*` committed (gitignored by design).

## C. Build and tests (local, required)

- [ ] `npm ci` from clean.
- [ ] `npx tsc --noEmit` passes.
- [ ] `cargo test --manifest-path src-tauri/Cargo.toml` passes.
- [ ] `npm run ffmpeg:verify` passes against the 9.0.2 pin.
- [ ] `npx tauri build` succeeds; NSIS `.exe` exists under
      `src-tauri/target/release/bundle/nsis/`.
- [ ] `tools/hash-artifacts.ps1` run; `*.sha256` files exist next to the
      installer.

## D. Install test (clean machine, required)

- [ ] Fresh Windows 10/11 + WebView2: installer runs, app launches,
      widget parks bottom-right on first run.
- [ ] Image conversion works (e.g. PNG → JPG).
- [ ] Video conversion works (e.g. MP4 → MKV) with progress.
- [ ] Audio conversion works (e.g. WAV → MP3).
- [ ] Converted files land as `<name>_catalyst<ext>` next to originals.
- [ ] Tray menu (Settings nudge, Quit) works; window position persists.
- [ ] Install dir contains the FFmpeg sidecar; resource dir contains
      `THIRD-PARTY-NOTICES.md` + `GPL-3.0-or-later.txt`.
- [ ] Uninstall is clean (no tray/widget residue).

## E. Signing and SmartScreen (expected beta behavior)

- [ ] Beta is **unsigned** (all `bundle.windows` signing keys `null`).
      SmartScreen/Unknown-publisher warning observed and documented in
      the release notes — not treated as a failure.
- [ ] `Get-AuthenticodeSignature` shows `NotSigned` (baseline recorded).
- [ ] Post-beta signing owner + mechanism chosen (recommend Azure Trusted
      Signing; see `docs/SIGNING.md`); no certificates committed.

## F. GitHub release draft

- [ ] Actions → `release` workflow green on tag `v0.1.0-beta.1`.
- [ ] Draft release is **pre-release + draft** (`prerelease: true`,
      `releaseDraft: true`); title `Catalyst v0.1.0-beta.1 (Windows beta)`.
- [ ] `*.sha256` files from the `windows-beta-sha256` workflow artifact
      attached to the draft.
- [ ] Draft body edited with real hashes, `ffmpeg -version` line, exe
      SHA256 (from `ffmpeg-audit` artifact), and toolchain versions
      (Node/Rust/Tauri/CLI/runner).
- [ ] Draft published manually only after A–E are checked.

## G. Still blocking distribution (as of this commit)

1. **FFmpeg 9.0.2 pin applied and verified locally** — manifest pins
   9.0.2 (zip SHA256 `60f46726…`, exe SHA256 `3256173f…` after the
   2026-10-08 fetch; `npm run ffmpeg:verify` reports `FFmpeg sidecar
   OK`, and a local `npx tauri build` produced an installable NSIS
   `.exe`). The remaining step is CI exercising the same path end to end
   on the first `v0.1.0-beta.1` tag push — keep the release a draft
   until then.
2. **No code signature** — expected SmartScreen warnings; store listing
   impossible until Section E's follow-up lands.
3. **EULA governing-law blank + no attorney review** — blocks *paid*
   distribution, not the free beta; schedule counsel before charging.
4. **Release workflow never exercised end-to-end** — the first
   `v0.1.0-beta.1` tag push is the live test; keep the release a draft
   until the install test (D) passes.
