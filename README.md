# Multi Snip

**Multi Snip application by WebProgress.AI** — a Windows snipping tool that takes several snips and
combines them into one image (vertically, horizontally or as a grid), so you no longer have to paste
screenshots into Paint and line them up by hand.

Current version: **1.3.0** (see [CHANGELOG.md](CHANGELOG.md)).

## How to use

1. **Capture.** Click **✂ New Snip** (Ctrl+N), or press **Ctrl+Shift+S** anywhere in Windows (change it with
   **⌨ Hotkey…**), and drag a rectangle. **Keep snipping** takes several in a row (Esc to stop).
   **📜 Scrolling** (Ctrl+Shift+R) captures a whole page: select the area that scrolls and Multi Snip scrolls and
   stitches it. **Paste** (Ctrl+V) adds an image from another snipping tool as the next snip; you can also
   **Open…** or drag & drop image files.
2. **Arrange.** Drag snips in the list to reorder them (or Ctrl+Up / Ctrl+Down). Choose **Vertical**,
   **Horizontal** or **Grid** (Auto picks a square grid such as 3 × 3 or 5 × 5, or choose up to 20 columns), and set
   alignment, spacing, margin, background, border and "Match sizes". **Output size** shrinks the saved image when
   many snips make it very large.
3. **Draw and label** with the tool bar, like in Paint: **Pen**, **Marker** (highlighter), **Line**, **Arrow**,
   **Rectangle**, **Ellipse** (Fill for solid shapes), **Text**, and **numbered labels** — round **❶** or square
   **▣1** — that count up automatically (set the next number in **Next #**, or **1-2-3** to renumber them all).
   Pick any of the Paint colors or a custom one, and a line / text size. Drawings stay editable: use **Select** to
   move them (drag or arrow keys), double-click text or a label to change it, Delete to remove, **Eraser** to wipe.
   Changing the color or size while a drawing is selected changes that drawing. Undo / redo with Ctrl+Z / Ctrl+Y.
4. **Zoom** with the slider or the percentage box at the bottom right (Fit, 25 %–800 %), Ctrl + mouse wheel, or
   Ctrl+0 (fit) / Ctrl+1 (100 %). Drag with the middle mouse button to pan.
5. **Save / share.** **Save** (Ctrl+S) writes a PNG that any app can show, but that also keeps the original snips,
   their order, the drawings and the options inside it. **Open** that PNG again in Multi Snip and everything is
   separate and editable again (like GIMP's .xcf layers, but in one normal image file). New file names get a
   timestamp, e.g. `MultiSnip_2026-01_11-18-20-00.png`. Once saved, the full path is shown in the title bar and
   Ctrl+S keeps saving to that same file; **Save As** (F12) offers a new timestamped name in the same folder.
   **Export** (Ctrl+E) writes a plain PNG / JPG / BMP / GIF. **Copy Combined** (Ctrl+C) or **⇱ Drag Out** puts the
   finished image into email, chat or documents.
6. **Tabs.** Work on several projects at once: **➕ Tab** (Ctrl+T), Ctrl+Tab to switch, Ctrl+W (or middle-click) to
   close. Each tab has its own snips, options, undo history and save location. Right-click snips to move them to
   another tab. Open tabs (saved or not) come back automatically the next time you start Multi Snip.

**File → Open Recent** and **File → Recent Folders** keep a history of the last saved files and folders; the recent
folders also appear in the left pane of the Save / Open dialogs.

| Shortcut | Action |
|---|---|
| Ctrl+Shift+S | Snip from anywhere (global hotkey, works while minimized; change it with **⌨ Hotkey…**) |
| Ctrl+N / Ctrl+Shift+N / Ctrl+Shift+R | New snip / full-screen snip / scrolling capture |
| Enter / Esc (in overlay) | Snip whole screen / cancel (or finish "Keep snipping") |
| Ctrl+T, Ctrl+W, Ctrl+Tab | New tab, close tab, next tab |
| Ctrl+O, Ctrl+S, F12, Ctrl+E | Open, save, save as, export plain image |
| Ctrl+V, Ctrl+C | Paste image as next snip, copy combined image |
| Ctrl+Z, Ctrl+Y | Undo, redo |
| Delete | Remove the selected drawing (or the selected snips) |
| Ctrl+Up / Ctrl+Down | Reorder snips |
| V P M E L A R O T C Q | Tools: Select, Pen, Marker, Eraser, Line, Arrow, Rectangle, Ellipse, Text, round label, square label |
| Shift while drawing | Straight lines at 45° steps, squares, circles |
| Ctrl + wheel, Ctrl+0, Ctrl+1 | Zoom, fit, 100 % |

Note: some apps and websites strip extra data from images they receive. The copy you **share** may then be a plain
image; your saved file on disk keeps everything.

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
| File version | 1.3.0.0 |
| Product name | Multi Snip application by WebProgress.AI |
| Product version | 1.3.0 |
| Copyright | Copyright © 2026 WebProgress.AI (idea and IP initiator). All rights reserved. |
| Company | WebProgress.AI |

These live in `src/MultiSnip/MultiSnip.csproj`. To release a new version, bump `<Version>`, `<FileVersion>`,
`<AssemblyVersion>` and `<InformationalVersion>` there and add an entry to `CHANGELOG.md`.

## Project layout

- `src/MultiSnip/MainForm*.cs` — main window: tabs, menus, tool bars, snip list, options, open/save, hotkey, tray icon
- `src/MultiSnip/EditorCanvas.cs`, `AnnotationRenderer.cs`, `Annotation.cs` — zoomable canvas and the drawing tools
- `src/MultiSnip/Project*.cs`, `EditablePng.cs` — tabs' data, undo, and the editable PNG format
- `src/MultiSnip/SelfTest.cs` — `MultiSnip.exe --self-test`, run by the build on Windows
- `src/MultiSnip/SnipOverlayForm.cs` — full-screen selection overlay (multi-monitor, high-DPI aware)
- `src/MultiSnip/ScrollingCapture.cs`, `ScrollStitcher.cs` — scrolling capture and frame stitching
- `tests/MultiSnip.Tests` — unit tests for stitching, layout, file names and the project format (run in CI)
- `src/MultiSnip/CombineLayout.cs`, `ImageCombiner.cs` — vertical / horizontal / grid layout and drawing
- `src/MultiSnip/Settings.cs` — remembered preferences
- `tools/make_icon.py` — regenerates the app icon
