# Changelog

All notable changes to Multi Snip (by WebProgress.AI). The version is set in
`src/MultiSnip/MultiSnip.csproj` (`<Version>`, `<FileVersion>`, `<AssemblyVersion>`);
bump it there and add an entry here for every release.

## [1.3.0] - 2026-11-10

### Added
- **Drawing and labels, like Paint**: pen, marker (highlighter), eraser, line, arrow, rectangle, ellipse (outline or
  filled), text labels (bold, optional white box), and **numbered round / square labels** that count up
  automatically, with "Next #" and "Renumber 1-2-3". The 20 Paint colors plus custom colors; line and text sizes.
  Drawings stay editable objects: select, move (mouse or arrow keys), restyle, edit text, delete; Undo / Redo.
- **Tabs**: several projects at once, each with its own snips, options, undo history and save location. Move snips
  between tabs. Open tabs are restored automatically after a restart.
- **Zoom**: slider and percentage box (Fit, 25 %–800 %), Ctrl + mouse wheel, Ctrl+0 / Ctrl+1, middle-button panning.
- **Editable save files**: Save writes a normal PNG that also stores the original snips, their order, drawings and
  options. Opening it in Multi Snip restores everything for re-arranging and editing.
- File names get a timestamp suffix (`_2026-01_11-18-20-00`); once saved, the full path is shown in the title bar
  and Ctrl+S keeps saving to the same file. Save As (F12), Export plain image (Ctrl+E).
- **Open Recent** files and **Recent Folders** history (also shown in the file dialogs).
- Grid: **Auto** (square grids like 3 × 3, 4 × 4, 5 × 5…) or up to 20 columns, with the resulting size shown.
- **Output size** (100 % / 75 % / 50 % / 33 % / 25 %) to keep large combinations manageable.
- Menus (File, Edit, Capture, View, Help), "Drag Out" button, copy/save a single snip with its drawings.
- Built-in self-test (`MultiSnip.exe --self-test`) that the build runs on Windows.

### Changed
- Paste (Ctrl+V) adds the clipboard image as the next snip of the current tab; pasting or dropping a saved
  Multi Snip PNG opens it as a tab.
- The Delete key removes the selected drawing first, otherwise the selected snips. Removing snips can be undone.

## [1.2.0] - 2026-10-13

### Added
- **Scrolling capture** (📜 Scrolling, Ctrl+Shift+R, or the tray menu): captures a whole web page, document or
  chat, not just the visible part. Select the area that scrolls; Multi Snip scrolls it down with the mouse wheel,
  captures each step and stitches everything into one tall image. Sticky headers/footers are kept only once.
  It stops by itself at the end of the page (or at 20,000 px); press Esc to stop early.
- "Scroll to the top first" option (in the Scrolling button's drop-down, on by default).

## [1.1.0] - 2026-10-11

### Added
- Configurable global snip hotkey: click **⌨ Hotkey…** in the toolbar (or the hotkey in the status bar, or
  "Change Hotkey…" in the tray menu) and press the combination you want. Backspace turns the hotkey off;
  "Reset" restores Ctrl+Shift+S. Combinations already taken by Windows/another app, or by Multi Snip's own
  shortcuts, are rejected with an explanation. The choice is remembered.

### Changed
- Default global hotkey is **Ctrl+Shift+S**.

## [1.0.0] - 2026-10-10

### Added
- Rectangle snipping across all monitors (frozen-screen overlay, size readout, crosshair).
- Full-screen snip of the monitor under the mouse; Enter in the overlay does the same.
- "Keep snipping" mode: take several snips in a row, Esc to finish.
- Optional capture delay (3 / 5 / 10 seconds) for menus and tooltips.
- Global hotkey **Ctrl+Shift+S** that snips even while the app is minimized or in the tray.
- Snip list with thumbnails: drag to reorder, move up/down/top/bottom, remove, copy or save one snip.
- Combine **vertically**, **horizontally** or in a **grid** (choose the number of columns), with live preview.
- Options: alignment, spacing, outer margin, background color or transparent, per-snip border (color/width),
  "match sizes" (same width / height / cell size).
- Copy the combined image to the clipboard (bitmap + PNG with transparency), save as PNG / JPG / BMP / GIF,
  or drag the preview straight into another app.
- Add images by pasting (Ctrl+V), Open…, or drag & drop from Explorer.
- Settings are remembered between runs (%APPDATA%\MultiSnip\settings.json).
- File properties: description "Multi Snip tool for better productivity",
  product "Multi Snip application by WebProgress.AI", company/copyright WebProgress.AI.
