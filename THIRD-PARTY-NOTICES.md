# Third-Party Notices

Catalyst is proprietary software (see `LICENSE`). This file documents
third-party components that Catalyst builds on or distributes. Each listed
component remains under its own license; nothing here transfers ownership
of third-party software to Catalyst, and the Catalyst proprietary license
does not cover these components.

Conventions used below:

- **Redistributed** means the component (or a build of it) ships inside an
  official Catalyst release bundle.
- **Build/dev-only** means the component is used to develop or build
  Catalyst but does not ship in the release bundle.
- Items marked **Needs verification before commercial distribution** are
  questions the repository alone cannot answer. They must be resolved
  before the first paid release.

## FFmpeg sidecar (redistributed)

- **Component:** FFmpeg command-line binary, invoked by Catalyst as a
  separate sidecar process for video/audio conversion
  (`convert_media` in `src-tauri/src/main.rs` spawns it; it is not linked
  into Catalyst code).
- **Build used (pinned for the first public Windows beta):**
  `ffmpeg 9.0.2-essentials_build-www.gyan.dev` (gyan.dev release build,
  dated 2026-09-19; upstream FFmpeg 9.0.2 "Lei", released 2026-09-18;
  built with gcc 16.2.0, libraries 61.1.102/63.1.102/12.1.102).
  - Pin file: `tools/ffmpeg-manifest.json` (`pinnedVersion: "9.0.2"`,
    `expectedVersionString:
    "ffmpeg version 9.0.2-essentials_build-www.gyan.dev"`).
  - Download URL (pinned):
    `https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip`
  - Zip SHA256 (pinned, from
    `https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip.sha256`):
    `60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba`
  - Upstream source commit for this release (per gyan.dev builds page):
    `https://github.com/FFmpeg/FFmpeg/commit/946fcce07b`
  - Previous development pin (superseded): `ffmpeg
    9.0.1-essentials_build-www.gyan.dev`. Do not ship 9.0.1 in the beta;
    the fetch script replaces a stale cached exe automatically.
- **How it enters the project (reproducible):**
  1. `src-tauri/build.rs` runs before `tauri_build` and calls
     `tools/fetch-ffmpeg.ps1 -ManifestPath tools/ffmpeg-manifest.json
     -DestinationExe src-tauri/binaries/ffmpeg-x86_64-pc-windows-msvc.exe`.
  2. The script downloads the pinned zip, fails the build on any SHA256
     mismatch, extracts only `ffmpeg.exe`, then fails the build unless
     `ffmpeg -version` contains the pinned `expectedVersionString`.
  3. It writes an audit file next to the exe
     (`ffmpeg-x86_64-pc-windows-msvc.exe.version.json`) recording the
     `ffmpeg -version` first line, the exe SHA256, the zip URL/hash, and
     the fetch timestamp.
  4. `src-tauri/binaries/` stays gitignored, so the binary itself is
     never committed; it becomes part of official release bundles at
     packaging time through `externalBin: ["binaries/ffmpeg"]` in
     `src-tauri/tauri.conf.json`.
  5. Verify a cached copy without downloading: `npm run ffmpeg:verify`
     (checks version string, `--enable-gpl --enable-version3`, `ffmpeg -L`
     GPL notice, and prints the exe SHA256). Force a clean re-fetch:
     delete `src-tauri/binaries/ffmpeg-*.exe*` and rebuild, or run the
     fetch script with `-Force`. Offline builds: `CATALYST_SKIP_FFMPEG=1`
     skips the download (media conversion then needs ffmpeg on PATH).
- **Applicable license: GNU General Public License v3 or later
  (SPDX: GPL-3.0-or-later).** The binary reports `--enable-gpl
  --enable-version3` in its build configuration (verified by running the
  bundled binary with `-version`), and `ffmpeg -L` prints the GPL
  redistribution notice ("either version 3 of the License, or (at your
  option) any later version"). The publisher of the gyan.dev builds
  licenses them under the GPL. This determination comes from the binary
  itself, not from an assumption.
- **What GPLv3 requires when distributing this binary, and how the beta
  satisfies it:**
  1. Preserve FFmpeg copyright notices — `ffmpeg -version`/`-L` output
     is preserved verbatim in the shipped sidecar; do not strip it.
  2. Include a copy of the GPL license text with the distribution — the
     verbatim text is vendored at `licenses/GPL-3.0-or-later.txt`
     (downloaded from `https://www.gnu.org/licenses/gpl-3.0.txt`) and
     shipped inside the installer via `bundle.resources` in
     `src-tauri/tauri.conf.json`, alongside this file and `EULA.md`.
  3. Make the Corresponding Source for the exact shipped binary
     available as GPLv3 Section 6 requires — Section 6(d) designated
     place: every GitHub release notes the exact FFmpeg version, zip
     SHA256, and links `https://ffmpeg.org/download.html`,
     `https://www.gyan.dev/ffmpeg/builds/`, and the exact source commit
     above. This file plus the vendored license text ship inside the
     bundle (`bundle.resources`), so the offer travels with the binary.
- **Source locations:** FFmpeg upstream sources at
  `https://ffmpeg.org/download.html`; gyan.dev publishes build scripts and
  source references at `https://www.gyan.dev/ffmpeg/builds/`.
- **Needs verification before commercial distribution (residual):**
  1. This pinning/verification/notice pipeline is implemented, but it has
     not had attorney review. Consult qualified counsel before relying on
     it for paid distribution, and keep the release-notes source links
     live for as long as the beta is distributed.
  2. Confirm no additional GPL-licensed code is introduced through other
     means (e.g. a locally built replacement binary or a switch from the
     `essentials` to the `full` variant would need its own
     build-configuration and library audit before release).

## Rust crates (compiled into the release binary)

Licenses verified against the crate manifests in the local Cargo registry
cache; versions are those pinned in `src-tauri/Cargo.lock`. Transitive
dependencies of these crates retain their own licenses (see the lock file
for the full set).

| Crate | Version | License |
| --- | --- | --- |
| `tauri` | 2.11.5 | Apache-2.0 OR MIT |
| `tauri-build` (build dependency) | 2.6.3 | Apache-2.0 OR MIT |
| `image` | 0.25.10 | MIT OR Apache-2.0 |
| `tokio` | 1.53.1 | MIT |
| `serde` | 1.0.229 | MIT OR Apache-2.0 |
| `serde_json` | 1.0.151 | MIT OR Apache-2.0 |

## npm packages

Licenses read from the installed packages' own `package.json` files.
`@tauri-apps/api` ships in the application frontend; the rest are
build/dev-only.

| Package | Version | License | Ships in release |
| --- | --- | --- | --- |
| `@tauri-apps/api` | 2.11.1 | Apache-2.0 OR MIT | Yes (frontend bundle) |
| `@tauri-apps/cli` | 2.11.4 | Apache-2.0 OR MIT | No (dev tool) |
| `typescript` | 5.6.3 | Apache-2.0 | No (dev tool) |
| `vite` | 6.4.3 | MIT | No (dev tool) |

## Fonts, icons, and media

- **Fonts:** none are bundled. The widget and website use system font
  stacks only (verified: no font files or webfont references in the
  repository).
- **Application icons** (`src-tauri/icons/`, generated from the
  nucleus-and-ring artwork) and the website demo recording
  (`landing/demo.mp4`) are project-created assets covered by the Catalyst
  proprietary license, per the repository history that introduced them.
- **WPF prototype assets** (`wpf-prototype/Assets/`) are project-created
  materials from the archived prototype, same treatment.
