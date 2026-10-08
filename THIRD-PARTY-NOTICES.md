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
- **Build used:** `ffmpeg 9.0.1-essentials_build-www.gyan.dev`, built with
  gcc 16.1.0, downloaded at first build time by `src-tauri/build.rs` via
  `tools/fetch-ffmpeg.ps1` from
  `https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip` and
  packaged through `externalBin: ["binaries/ffmpeg"]` in
  `src-tauri/tauri.conf.json`.
- **How it enters the project:** downloaded during the build; the
  `src-tauri/binaries/` directory is gitignored, so the binary is not
  stored in this repository. It becomes part of official release bundles
  at packaging time.
- **Applicable license: GNU General Public License v3 (GPLv3).** The
  binary reports `--enable-gpl --enable-version3` in its build
  configuration (verified by running the bundled binary with `-version`),
  and the publisher of the gyan.dev builds licenses them under the GPL.
  This determination comes from the binary itself, not from an assumption.
- **What GPLv3 requires when distributing this binary:**
  1. Preserve FFmpeg copyright notices.
  2. Include a copy of the GPLv3 license text with the distribution.
  3. Make the Corresponding Source for the exact binary available as
     GPLv3 Section 6 requires (source offer / documented source location).
- **Source locations:** FFmpeg upstream sources at
  `https://ffmpeg.org/download.html`; gyan.dev publishes build scripts and
  source references at `https://www.gyan.dev/ffmpeg/builds/`.
- **Needs verification before commercial distribution:**
  1. Pin the exact FFmpeg build per release and record its checksum, so
     the Corresponding Source offer always matches the shipped binary.
  2. Decide the source-offer mechanism (written offer in the installer
     vs. a durable download page) and confirm the full GPLv3 text ships
     inside the paid bundle, not just in this repository.
  3. Confirm no additional GPL-licensed code is introduced through other
     means (e.g. a locally built replacement binary would need its own
     build-configuration audit).

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
