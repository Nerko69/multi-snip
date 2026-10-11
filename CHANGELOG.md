# Changelog

All notable changes to Multi Snip (by WebProgress.AI). The version is set in
`src/MultiSnip/MultiSnip.csproj` (`<Version>`, `<FileVersion>`, `<AssemblyVersion>`);
bump it there and add an entry here for every release.

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
