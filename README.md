# Multi Snip

**Multi Snip application by WebProgress.AI** — a Windows snipping tool that takes several snips and
combines them into one image (vertically, horizontally or as a grid), so you no longer have to paste
screenshots into Paint and line them up by hand.

Current version: **1.0.0** (see [CHANGELOG.md](CHANGELOG.md)).

## How to use

1. Click **✂ New Snip** (or press **Ctrl+N**, or **Ctrl+Alt+S** from anywhere in Windows) and drag a rectangle.
   Repeat for every piece you need. Turn on **Keep snipping** to take several in a row (Esc to stop).
2. Reorder the snips by dragging them in the list (or Ctrl+Up / Ctrl+Down).
3. Choose **Vertical**, **Horizontal** or **Grid**, then adjust alignment, spacing, margin, background, border and
   "Match sizes". The preview updates as you change them.
4. **Copy Combined** (Ctrl+C) and paste it anywhere, **Save As…** (Ctrl+S), or drag the preview straight into
   an email, chat or document.

You can also add existing images with **Paste** (Ctrl+V), **Open…** (Ctrl+O), or by dropping files onto the window.

| Shortcut | Action |
|---|---|
| Ctrl+Alt+S | Snip from anywhere (global hotkey, works while minimized) |
| Ctrl+N / Ctrl+Shift+N | New snip / full-screen snip |
| Enter / Esc (in overlay) | Snip whole screen / cancel (or finish "Keep snipping") |
| Ctrl+V, Ctrl+O | Paste image, open images |
| Ctrl+C, Ctrl+S | Copy / save combined image |
| Ctrl+Up / Ctrl+Down, Delete | Reorder, remove selected snip |

## Download / build

Every push runs the **Build Multi Snip** GitHub Action on Windows. Its artifact contains:

- `MultiSnip-win-x64.zip` — a single `MultiSnip.exe` that runs without installing anything (larger file).
- `MultiSnip-win-x64-small.zip` — a small `MultiSnip.exe` that needs the
  [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

Pushing a tag such as `v1.0.0` also publishes those zips as a GitHub Release.

To build locally on Windows with the .NET 8 SDK:

```powershell
dotnet publish src/MultiSnip/MultiSnip.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -o publish
```

Build on Windows (or in the GitHub Action). The SDK only writes the file-properties metadata into the `.exe` when it builds on Windows.

## File properties (Explorer → Properties → Details)

| Field | Value |
|---|---|
| File description | Multi Snip tool for better productivity |
| File version | 1.0.0.0 |
| Product name | Multi Snip application by WebProgress.AI |
| Product version | 1.0.0 |
| Copyright | Copyright © 2026 WebProgress.AI (idea and IP initiator). All rights reserved. |
| Company | WebProgress.AI |

These live in `src/MultiSnip/MultiSnip.csproj`. To release a new version, bump `<Version>`, `<FileVersion>`,
`<AssemblyVersion>` and `<InformationalVersion>` there and add an entry to `CHANGELOG.md`.

## Project layout

- `src/MultiSnip/MainForm.cs` — main window: snip list, combine options, preview, clipboard/save, hotkey, tray icon
- `src/MultiSnip/SnipOverlayForm.cs` — full-screen selection overlay (multi-monitor, high-DPI aware)
- `src/MultiSnip/ImageCombiner.cs` — vertical / horizontal / grid combining logic
- `src/MultiSnip/Settings.cs` — remembered preferences
- `tools/make_icon.py` — regenerates the app icon
