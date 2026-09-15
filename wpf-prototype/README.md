# Catalyst (prototype v0)

A floating always-on-top "nucleus" widget you drag image files onto to convert them.
This is a minimal proof-of-concept for the core interaction, not a finished app.

## What it does

- Sits in the bottom-right corner of your screen as a small blue circle (the "nucleus").
- Drag an image file (.jpg/.jpeg/.png/.bmp/.gif/.tiff) over it:
  - Up to 3 small format nodes appear in a ring around it — the available target
    formats, excluding whatever format the file already is.
  - Move the cursor near whichever node you want; it highlights as the nearest one.
  - Drop, and the file converts to whichever node was highlighted at that moment.
- The converted file is saved next to the original as `<name>_catalyst<ext>`.
- Left-click and drag the widget itself to reposition it anywhere on screen — its
  position is remembered between launches.
- A system tray icon (the default Windows app icon for now) has a right-click menu
  with **Settings** (placeholder) and **Quit**.

## Requirements

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download) (or Visual Studio 2022 with the
  ".NET desktop development" workload, which includes it)

## Running it

**Visual Studio:** open `Catalyst.csproj`, press F5.

**Command line:**
```
dotnet run
```
from inside this folder.

## Known limitations (this is v0.2)

- Only image formats, only via `System.Drawing.Common` (Windows-only API, which is fine
  since this is a Windows app, but it's a fairly basic encoder — no quality/compression
  controls yet).
- Tray icon uses the generic Windows application icon as a placeholder — no custom icon yet.
- Tray "Settings" menu item is a placeholder (shows a message box, does nothing yet).
- All dropped files in a multi-file drop convert to the same picked format, regardless
  of each file's own type — fine for a same-type batch, not smart about mixed drops yet.
- No app icon for the window/taskbar, no installer/packaging.

## Suggested next steps, roughly in order

1. **Video/audio conversion** — bundle an `ffmpeg.exe` binary and shell out to it for
   non-image formats; same drop-handling code path, different converter behind it.
2. **Batch mode** — visual feedback for multi-file drops (progress per file, not just
   a final count), and per-file format resolution for mixed-type drops.
3. **Undo / history popover** — small list of recent conversions with "open containing
   folder" links, shown briefly after a drop.
4. **App icon + packaging** — design the nucleus mark as a proper .ico (used for both
   the window and the tray icon), package with
   `dotnet publish -r win-x64 --self-contained` or wrap with an installer (Velopack
   works well for auto-update later).
5. **Code signing** — needed before public distribution, or Windows SmartScreen will
   warn downloaders about an "unknown publisher."
