namespace MultiSnip;

public enum SnipMode { Region, FullScreen, Scrolling }

/// <summary>
/// Main window. Split over several files:
/// MainForm.cs (layout), MainForm.Tabs.cs (tabs, open/save, session), MainForm.Snips.cs (snip list, capture,
/// clipboard, rendering) and MainForm.Input.cs (keyboard, global hotkey, window lifetime).
/// </summary>
public sealed partial class MainForm : Form
{
    private const int HotkeyId = 0x5A01;
    private const string InternalDragFormat = "MultiSnip.ItemIndex";

    /// <summary>The colors of the Paint palette.</summary>
    private static readonly Color[] Palette =
    {
        Color.FromArgb(0, 0, 0), Color.FromArgb(127, 127, 127), Color.FromArgb(136, 0, 21), Color.FromArgb(237, 28, 36),
        Color.FromArgb(255, 127, 39), Color.FromArgb(255, 242, 0), Color.FromArgb(34, 177, 76), Color.FromArgb(0, 162, 232),
        Color.FromArgb(63, 72, 204), Color.FromArgb(163, 73, 164),
        Color.FromArgb(255, 255, 255), Color.FromArgb(195, 195, 195), Color.FromArgb(185, 122, 87), Color.FromArgb(255, 174, 201),
        Color.FromArgb(255, 201, 14), Color.FromArgb(239, 228, 176), Color.FromArgb(181, 230, 29), Color.FromArgb(153, 217, 234),
        Color.FromArgb(112, 146, 190), Color.FromArgb(200, 191, 231),
    };

    private static readonly int[] ZoomPresets = { 25, 50, 75, 100, 125, 150, 200, 300, 400, 800 };

    private readonly Settings _settings = Settings.Load();
    private readonly List<Project> _projects = new();
    private Project _project = null!; // the active tab
    private Bitmap? _base;
    private CombinedLayout? _layout;
    private bool _snipping;
    private bool _loadingUi;
    private bool _syncingZoom;
    private bool _hotkeyRegistered;
    private string? _pendingNote; // extra info shown once with the next status update

    // UI
    private readonly MenuStrip _menu = new();
    private readonly ToolStrip _captureBar = new();
    private readonly ToolStrip _drawBar = new();
    private readonly TabControl _tabs = new();
    private readonly ToolStripComboBox _delayCombo = new();
    private readonly ToolStripButton _keepSnippingButton = new("Keep snipping");
    private readonly ToolStripMenuItem _keepSnippingMenu = new("Keep snipping");
    private readonly ToolStripMenuItem _scrollTopMenu = new("Scrolling: scroll to the top first");
    private readonly ToolStripMenuItem _recentFilesMenu = new("Open &Recent");
    private readonly ToolStripMenuItem _recentFoldersMenu = new("Recent &Folders");
    private readonly ToolStripMenuItem _timestampMenu = new("Add &timestamp to new file names");
    private readonly ToolStripMenuItem _undoMenu = new("&Undo");
    private readonly ToolStripMenuItem _redoMenu = new("&Redo");
    private readonly ToolStripButton _undoButton = new("↶") { ToolTipText = "Undo (Ctrl+Z)" };
    private readonly ToolStripButton _redoButton = new("↷") { ToolTipText = "Redo (Ctrl+Y)" };
    private readonly ListView _list = new();
    private readonly ImageList _thumbs = new();
    private readonly EditorCanvas _canvas = new() { Dock = DockStyle.Fill };

    // combine options
    private readonly RadioButton _vertical = new() { Text = "Vertical", AutoSize = true };
    private readonly RadioButton _horizontal = new() { Text = "Horizontal", AutoSize = true };
    private readonly RadioButton _grid = new() { Text = "Grid", AutoSize = true };
    private readonly ComboBox _columns = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 95 };
    private readonly Label _gridInfo = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly ComboBox _align = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly NumericUpDown _spacing = new() { Minimum = 0, Maximum = 500, Width = 60 };
    private readonly NumericUpDown _margin = new() { Minimum = 0, Maximum = 500, Width = 60 };
    private readonly CheckBox _matchSize = new() { Text = "Match sizes", AutoSize = true };
    private readonly Button _bgColor = new() { Width = 40, FlatStyle = FlatStyle.Flat };
    private readonly CheckBox _transparent = new() { Text = "Transparent", AutoSize = true };
    private readonly CheckBox _border = new() { Text = "Border", AutoSize = true };
    private readonly Button _borderColor = new() { Width = 40, FlatStyle = FlatStyle.Flat };
    private readonly NumericUpDown _borderWidth = new() { Minimum = 1, Maximum = 50, Width = 50 };
    private readonly ComboBox _outputScale = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };

    // drawing tools
    private readonly Dictionary<DrawTool, ToolStripButton> _toolButtons = new();
    private readonly ToolStripComboBox _sizeCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 62 };
    private readonly ToolStripComboBox _fontCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 62 };
    private readonly ToolStripButton _fillButton = new("Fill") { CheckOnClick = true, ToolTipText = "Filled shapes / text on a white box" };
    private readonly ToolStripButton _boldButton = new("B") { CheckOnClick = true, ToolTipText = "Bold text" };
    private readonly NumericUpDown _nextBadge = new() { Minimum = 0, Maximum = 9999, Width = 52 };
    private readonly ToolStripButton _colorButton = new() { DisplayStyle = ToolStripItemDisplayStyle.Image, ToolTipText = "Current color — click for more colors" };

    // status bar
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _hotkeyStatus = new() { IsLink = true, ToolTipText = "Click to change the global snip hotkey" };
    private readonly TrackBar _zoomSlider = new() { Minimum = 0, Maximum = 100, TickStyle = TickStyle.None, AutoSize = false, Width = 140, Height = 22 };
    private readonly ToolStripComboBox _zoomCombo = new() { AutoSize = false, Width = 72, ToolTipText = "Zoom (Ctrl + mouse wheel)" };

    private readonly NotifyIcon _tray = new();
    private readonly System.Windows.Forms.Timer _renderTimer = new() { Interval = 120 };

    public MainForm()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = $"Multi Snip v{AppInfo.Version}";
        Icon = AppInfo.LoadIcon();
        MinimumSize = new Size(900, 560);
        Size = new Size(1280, 820);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        AllowDrop = true;

        BuildMenu();
        BuildCaptureBar();
        BuildDrawBar();
        BuildTabs();

        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 6 };
        split.Panel1.Controls.Add(BuildSnipList());
        split.Panel2.Controls.Add(BuildWorkArea());

        Controls.Add(split);
        Controls.Add(_tabs);
        Controls.Add(_drawBar);
        Controls.Add(_captureBar);
        Controls.Add(BuildStatusBar());
        Controls.Add(_menu);
        MainMenuStrip = _menu;
        BuildTray();
        ResumeLayout(true);
        split.SplitterDistance = LogicalToDeviceUnits(240);

        LoadToolSettings();
        _renderTimer.Tick += (_, _) => { _renderTimer.Stop(); RenderPreview(); };
        DragEnter += OnExternalDragEnter;
        DragDrop += OnExternalDragDrop;

        RestoreSession();
        if (_projects.Count == 0) NewTab();
    }

    // ------------------------------------------------------------------ menus and toolbars

    private static ToolStripMenuItem Item(string text, string? shortcut, Action onClick)
    {
        // Shortcuts are handled in ProcessCmdKey (so Ctrl+C etc. still work inside text boxes); only displayed here.
        var item = new ToolStripMenuItem(text) { ShortcutKeyDisplayString = shortcut };
        item.Click += (_, _) => onClick();
        return item;
    }

    private void BuildMenu()
    {
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("&New Tab", "Ctrl+T", () => NewTab()),
            Item("&Open…", "Ctrl+O", OpenFiles),
            _recentFilesMenu,
            new ToolStripSeparator(),
            Item("&Save", "Ctrl+S", () => Save()),
            Item("Save &As…", "F12", () => SaveAs()),
            Item("&Export Image As… (PNG, JPG, BMP, GIF)", "Ctrl+E", ExportImage),
            _recentFoldersMenu,
            _timestampMenu,
            new ToolStripSeparator(),
            Item("&Close Tab", "Ctrl+W", () => CloseTab(_project)),
            Item("E&xit", "Alt+F4", Close),
        });
        _timestampMenu.CheckOnClick = true;
        _timestampMenu.CheckedChanged += (_, _) => _settings.AddTimestamp = _timestampMenu.Checked;
        file.DropDownOpening += (_, _) => { RebuildRecentMenus(); _timestampMenu.Checked = _settings.AddTimestamp; };

        var edit = new ToolStripMenuItem("&Edit");
        _undoMenu.ShortcutKeyDisplayString = "Ctrl+Z";
        _undoMenu.Click += (_, _) => Undo();
        _redoMenu.ShortcutKeyDisplayString = "Ctrl+Y";
        _redoMenu.Click += (_, _) => Redo();
        edit.DropDownItems.AddRange(new ToolStripItem[]
        {
            _undoMenu, _redoMenu,
            new ToolStripSeparator(),
            Item("&Copy Combined Image", "Ctrl+C", CopyCombined),
            Item("&Paste Image as Next Snip", "Ctrl+V", PasteFromClipboard),
            new ToolStripSeparator(),
            Item("&Delete Selected Drawing / Snip", "Del", DeleteSelected),
            Item("Clear Drawings on Selected Snips", null, ClearDrawingsOnSelected),
            Item("Re&number Badges 1, 2, 3…", null, RenumberBadges),
            new ToolStripSeparator(),
            Item("Remove &All Snips", null, ClearAll),
        });
        edit.DropDownOpening += (_, _) => UpdateUndoState();

        var capture = new ToolStripMenuItem("&Capture");
        _keepSnippingMenu.CheckOnClick = true;
        _keepSnippingMenu.CheckedChanged += (_, _) => _keepSnippingButton.Checked = _keepSnippingMenu.Checked;
        _scrollTopMenu.CheckOnClick = true;
        _scrollTopMenu.CheckedChanged += (_, _) => _settings.ScrollToTopFirst = _scrollTopMenu.Checked;
        capture.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("&New Snip", "Ctrl+N", () => _ = SnipAsync(SnipMode.Region)),
            Item("&Full Screen Snip", "Ctrl+Shift+N", () => _ = SnipAsync(SnipMode.FullScreen)),
            Item("&Scrolling Capture (whole page)", "Ctrl+Shift+R", () => _ = SnipAsync(SnipMode.Scrolling)),
            new ToolStripSeparator(),
            _keepSnippingMenu,
            _scrollTopMenu,
            new ToolStripSeparator(),
            Item("Global &Hotkey…", null, ChangeHotkey),
        });

        var view = new ToolStripMenuItem("&View");
        view.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("Zoom &In", "Ctrl++", () => _canvas.ZoomBy(1.25f)),
            Item("Zoom &Out", "Ctrl+-", () => _canvas.ZoomBy(0.8f)),
            Item("&100%", "Ctrl+1", () => _canvas.SetZoom(1f)),
            Item("&Fit to Window", "Ctrl+0", () => _canvas.SetZoom(null)),
            new ToolStripSeparator(),
            Item("Next Tab", "Ctrl+Tab", () => SwitchTab(+1)),
            Item("Previous Tab", "Ctrl+Shift+Tab", () => SwitchTab(-1)),
        });

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add(Item("&About Multi Snip", null, () => { using var f = new AboutForm(HotkeyText); f.ShowDialog(this); }));

        _menu.Items.AddRange(new ToolStripItem[] { file, edit, capture, view, help });
    }

    private static ToolStripButton Button(string text, string tip, Action onClick)
    {
        var b = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = tip };
        b.Click += (_, _) => onClick();
        return b;
    }

    private void BuildCaptureBar()
    {
        _captureBar.GripStyle = ToolStripGripStyle.Hidden;
        _captureBar.Padding = new Padding(4, 2, 4, 2);

        _captureBar.Items.Add(Button("✂  New Snip", "Drag a rectangle to snip (Ctrl+N)", () => _ = SnipAsync(SnipMode.Region)));
        _captureBar.Items.Add(Button("🖥  Full Screen", "Snip the whole screen the mouse is on (Ctrl+Shift+N)", () => _ = SnipAsync(SnipMode.FullScreen)));
        _captureBar.Items.Add(Button("📜  Scrolling", "Capture a whole page, not just the visible part (Ctrl+Shift+R).\n" +
                                                      "Select the area that scrolls; Multi Snip scrolls it and stitches everything.\nPress Esc to stop early.",
            () => _ = SnipAsync(SnipMode.Scrolling)));
        _captureBar.Items.Add(new ToolStripLabel("Delay:"));
        _delayCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _delayCombo.Items.AddRange(new object[] { "None", "3 sec", "5 sec", "10 sec" });
        _delayCombo.AutoSize = false;
        _delayCombo.Width = 70;
        _delayCombo.SelectedIndexChanged += (_, _) =>
            _settings.DelaySeconds = _delayCombo.SelectedIndex switch { 1 => 3, 2 => 5, 3 => 10, _ => 0 };
        _captureBar.Items.Add(_delayCombo);
        _keepSnippingButton.CheckOnClick = true;
        _keepSnippingButton.ToolTipText = "After each snip, immediately start the next one. Press Esc to stop.";
        _keepSnippingButton.CheckedChanged += (_, _) =>
        {
            _settings.KeepSnipping = _keepSnippingButton.Checked;
            _keepSnippingMenu.Checked = _keepSnippingButton.Checked;
        };
        _captureBar.Items.Add(_keepSnippingButton);
        _captureBar.Items.Add(new ToolStripSeparator());
        _captureBar.Items.Add(Button("➕ Tab", "New tab for another project (Ctrl+T)", () => NewTab()));
        _captureBar.Items.Add(Button("📂  Open…", "Open a saved Multi Snip image (as a tab with all its snips) or add images (Ctrl+O)", OpenFiles));
        _captureBar.Items.Add(Button("💾  Save", "Save this tab (Ctrl+S). The PNG keeps the original snips and drawings editable.", () => Save()));
        _captureBar.Items.Add(new ToolStripSeparator());
        _captureBar.Items.Add(Button("📋  Paste", "Add the image on the clipboard as the next snip (Ctrl+V)", PasteFromClipboard));
        _captureBar.Items.Add(Button("⧉  Copy Combined", "Copy the finished image to the clipboard (Ctrl+C)", CopyCombined));
        var drag = new ToolStripButton("⇱  Drag Out") { ToolTipText = "Drag this button into another app (chat, email, document) to drop the finished image there" };
        drag.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) StartDragOut(); };
        _captureBar.Items.Add(drag);

        var hotkey = Button("⌨  Hotkey…", "Change the global snip hotkey", ChangeHotkey);
        hotkey.Alignment = ToolStripItemAlignment.Right;
        _captureBar.Items.Add(hotkey);
    }

    private void BuildDrawBar()
    {
        _drawBar.GripStyle = ToolStripGripStyle.Hidden;
        _drawBar.Padding = new Padding(4, 1, 4, 1);
        int sw = LogicalToDeviceUnits(16);
        _drawBar.ImageScalingSize = new Size(sw, sw);

        _undoButton.Click += (_, _) => Undo();
        _redoButton.Click += (_, _) => Redo();
        _drawBar.Items.Add(_undoButton);
        _drawBar.Items.Add(_redoButton);
        _drawBar.Items.Add(new ToolStripSeparator());

        void AddTool(DrawTool tool, string text, string tip)
        {
            var b = new ToolStripButton(text) { ToolTipText = tip };
            b.Click += (_, _) => SetTool(tool);
            _toolButtons[tool] = b;
            _drawBar.Items.Add(b);
        }
        AddTool(DrawTool.Select, "↖ Select", "Select, move (drag or arrow keys) and delete drawings. Double-click text to edit. (V)");
        AddTool(DrawTool.Pen, "✏ Pen", "Draw freehand (P)");
        AddTool(DrawTool.Marker, "🖍 Marker", "Highlighter: translucent, thick strokes (M)");
        AddTool(DrawTool.Eraser, "⌫ Eraser", "Click or drag over drawings to remove them (E)");
        _drawBar.Items.Add(new ToolStripSeparator());
        AddTool(DrawTool.Line, "╱", "Line — hold Shift for 45° steps (L)");
        AddTool(DrawTool.Arrow, "➜", "Arrow — hold Shift for 45° steps (A)");
        AddTool(DrawTool.Rectangle, "▭", "Rectangle — hold Shift for a square (R)");
        AddTool(DrawTool.Ellipse, "◯", "Ellipse — hold Shift for a circle (O)");
        _fillButton.CheckedChanged += (_, _) =>
        {
            if (_loadingUi) return;
            _canvas.Filled = _settings.ToolFilled = _fillButton.Checked;
            _canvas.ApplyStyleToSelection(fill: true);
        };
        _drawBar.Items.Add(_fillButton);
        _drawBar.Items.Add(new ToolStripSeparator());
        AddTool(DrawTool.Text, "𝐓 Text", "Click to type a label; Enter to finish, Shift+Enter for a new line (T)");
        _boldButton.Font = new Font(_drawBar.Font, FontStyle.Bold);
        _boldButton.CheckedChanged += (_, _) =>
        {
            if (_loadingUi) return;
            _canvas.BoldText = _settings.ToolBold = _boldButton.Checked;
            _canvas.ApplyStyleToSelection(bold: true);
        };
        _drawBar.Items.Add(_boldButton);
        AddTool(DrawTool.BadgeCircle, "❶", "Numbered round label: click to place the next number (C). Double-click one to change it.");
        AddTool(DrawTool.BadgeSquare, "▣1", "Numbered square label: click to place the next number (Q). Double-click one to change it.");
        _drawBar.Items.Add(new ToolStripLabel("Next #"));
        _nextBadge.ValueChanged += (_, _) => { if (!_loadingUi) _project.NextBadgeNumber = (int)_nextBadge.Value; };
        _drawBar.Items.Add(new ToolStripControlHost(_nextBadge) { ToolTipText = "Number the next label gets" });
        _drawBar.Items.Add(Button("1-2-3", "Renumber all labels 1, 2, 3… in order (snip order, then placement order)", RenumberBadges));
        _drawBar.Items.Add(new ToolStripSeparator());

        _drawBar.Items.Add(new ToolStripLabel("Size"));
        foreach (int s in new[] { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48 }) _sizeCombo.Items.Add($"{s} px");
        _sizeCombo.ToolTipText = "Line / pen width";
        _sizeCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_loadingUi || _sizeCombo.SelectedItem is not string t) return;
            _canvas.LineWidth = _settings.ToolWidth = float.Parse(t.Split(' ')[0]);
            _canvas.ApplyStyleToSelection(width: true);
        };
        _drawBar.Items.Add(_sizeCombo);
        _drawBar.Items.Add(new ToolStripLabel("Text"));
        foreach (int s in new[] { 10, 12, 14, 16, 20, 24, 28, 32, 40, 48, 64, 80, 96 }) _fontCombo.Items.Add($"{s} px");
        _fontCombo.ToolTipText = "Text and label size";
        _fontCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_loadingUi || _fontCombo.SelectedItem is not string t) return;
            _canvas.FontSizePx = _settings.ToolFontSize = float.Parse(t.Split(' ')[0]);
            _canvas.ApplyStyleToSelection(font: true);
        };
        _drawBar.Items.Add(_fontCombo);
        _drawBar.Items.Add(new ToolStripSeparator());

        _colorButton.Click += (_, _) => PickToolColor();
        _drawBar.Items.Add(_colorButton);
        foreach (Color c in Palette)
        {
            var b = new ToolStripButton { DisplayStyle = ToolStripItemDisplayStyle.Image, Image = Swatch(c, sw, false), ToolTipText = ColorName(c), Margin = new Padding(0) };
            b.Click += (_, _) => SetToolColor(c);
            _drawBar.Items.Add(b);
        }
    }

    private static Bitmap Swatch(Color c, int size, bool current)
    {
        var bmp = new Bitmap(size, size);
        using Graphics g = Graphics.FromImage(bmp);
        using var b = new SolidBrush(c);
        g.FillRectangle(b, 1, 1, size - 2, size - 2);
        g.DrawRectangle(current ? Pens.Black : Pens.Gray, 0, 0, size - 1, size - 1);
        if (current) g.DrawRectangle(Pens.White, 1, 1, size - 3, size - 3);
        return bmp;
    }

    private static string ColorName(Color c) => $"RGB {c.R}, {c.G}, {c.B}";

    private void BuildTabs()
    {
        _tabs.Dock = DockStyle.Top;
        _tabs.Padding = new Point(14, 4);
        _tabs.ShowToolTips = true;
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_tabs.SelectedIndex >= 0 && _tabs.SelectedIndex < _projects.Count && !ReferenceEquals(_projects[_tabs.SelectedIndex], _project))
                ActivateProject(_projects[_tabs.SelectedIndex]);
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("New Tab", null, (_, _) => NewTab());
        menu.Items.Add("Close Tab", null, (_, _) => CloseTab(_project));
        menu.Items.Add("Close Other Tabs", null, (_, _) => { foreach (Project p in _projects.Where(p => p != _project).ToList()) if (!CloseTab(p)) break; });
        _tabs.ContextMenuStrip = menu;
        _tabs.MouseUp += (_, e) =>
        {
            // Middle-click closes a tab; right-click selects the tab before showing the menu.
            for (int i = 0; i < _tabs.TabCount; i++)
            {
                if (!_tabs.GetTabRect(i).Contains(e.Location)) continue;
                if (e.Button == MouseButtons.Middle) CloseTab(_projects[i]);
                else if (e.Button == MouseButtons.Right) _tabs.SelectedIndex = i;
                break;
            }
        };
    }

    private Control BuildSnipList()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(6) };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        panel.Controls.Add(new Label
        {
            Text = "Snips (drag to reorder)",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4),
        }, 0, 0);

        _thumbs.ColorDepth = ColorDepth.Depth32Bit;
        _thumbs.ImageSize = new Size(LogicalToDeviceUnits(150), LogicalToDeviceUnits(100));
        _list.Dock = DockStyle.Fill;
        _list.View = View.LargeIcon;
        _list.LargeImageList = _thumbs;
        _list.MultiSelect = true;
        _list.HideSelection = false;
        _list.AllowDrop = true;
        _list.ItemDrag += (_, e) =>
        {
            if (e.Item is ListViewItem item)
                _list.DoDragDrop(new DataObject(InternalDragFormat, item.Index), DragDropEffects.Move);
        };
        _list.DragEnter += OnListDragOver;
        _list.DragOver += OnListDragOver;
        _list.DragLeave += (_, _) => _list.InsertionMark.Index = -1;
        _list.DragDrop += OnListDragDrop;
        _list.ContextMenuStrip = BuildListMenu();
        panel.Controls.Add(_list, 0, 1);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 4, 0, 0) };
        buttons.Controls.Add(SmallButton("▲ Up", "Move selected up (Ctrl+Up)", () => MoveSelected(-1)));
        buttons.Controls.Add(SmallButton("▼ Down", "Move selected down (Ctrl+Down)", () => MoveSelected(1)));
        buttons.Controls.Add(SmallButton("✕ Remove", "Remove selected snips (Delete)", RemoveSelected));
        panel.Controls.Add(buttons, 0, 2);
        return panel;
    }

    private ContextMenuStrip BuildListMenu()
    {
        var menu = new ContextMenuStrip();
        var moveTab = new ToolStripMenuItem("Move to another tab");
        menu.Items.Add("Copy this snip (with drawings)", null, (_, _) => { if (Selected().FirstOrDefault() is { } s) { using Bitmap b = RenderSnip(s); SetClipboardImage(b); } });
        menu.Items.Add("Save this snip as…", null, (_, _) => { if (Selected().FirstOrDefault() is { } s) { using Bitmap b = RenderSnip(s); SaveImageFile(b, "Snip", out _); } });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Move up", null, (_, _) => MoveSelected(-1));
        menu.Items.Add("Move down", null, (_, _) => MoveSelected(1));
        menu.Items.Add("Move to top", null, (_, _) => MoveSelectedTo(0));
        menu.Items.Add("Move to bottom", null, (_, _) => MoveSelectedTo(_project.Snips.Count));
        menu.Items.Add(moveTab);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Clear drawings", null, (_, _) => ClearDrawingsOnSelected());
        menu.Items.Add("Remove", null, (_, _) => RemoveSelected());
        menu.Opening += (_, e) =>
        {
            e.Cancel = _list.SelectedIndices.Count == 0;
            moveTab.DropDownItems.Clear();
            foreach (Project p in _projects.Where(p => p != _project))
            {
                Project target = p;
                moveTab.DropDownItems.Add(p.DisplayName, null, (_, _) => MoveSelectedToTab(target));
            }
            moveTab.DropDownItems.Add("New tab", null, (_, _) => MoveSelectedToTab(null));
        };
        return menu;
    }

    private static Button SmallButton(string text, string tip, Action onClick)
    {
        var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(4, 0, 4, 0) };
        new ToolTip().SetToolTip(b, tip);
        b.Click += (_, _) => onClick();
        return b;
    }

    private Control BuildWorkArea()
    {
        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Padding = new Padding(6, 4, 6, 2),
        };

        _columns.Items.Add("Auto");
        foreach (int c in GridColumnChoices) _columns.Items.Add($"{c} columns");
        options.Controls.Add(Row(new Label { Text = "Combine:", AutoSize = true, Font = new Font(Font, FontStyle.Bold) },
            _vertical, _horizontal, _grid, _columns, _gridInfo));
        options.Controls.Add(Row(Lbl("Align"), _align));
        options.Controls.Add(Row(Lbl("Spacing"), _spacing));
        options.Controls.Add(Row(Lbl("Margin"), _margin));
        options.Controls.Add(Row(_matchSize));
        options.Controls.Add(Row(Lbl("Background"), _bgColor, _transparent));
        options.Controls.Add(Row(_border, _borderColor, _borderWidth));
        foreach (int s in OutputScales) _outputScale.Items.Add($"{s}%");
        options.Controls.Add(Row(Lbl("Output size"), _outputScale));

        var tips = new ToolTip();
        tips.SetToolTip(_columns, "Grid columns. Auto picks a square-ish grid (e.g. 9 snips → 3 × 3, 16 → 4 × 4).");
        tips.SetToolTip(_matchSize, "Vertical: scale snips to the same width. Horizontal: same height. Grid: same cell size.");
        tips.SetToolTip(_spacing, "Gap between snips, in pixels");
        tips.SetToolTip(_margin, "Outer padding around the combined image, in pixels");
        tips.SetToolTip(_transparent, "Transparent background (kept when saving PNG)");
        tips.SetToolTip(_borderWidth, "Border width, in pixels");
        tips.SetToolTip(_outputScale, "Scale the saved / copied image down — handy when many snips make it very large");

        foreach (var rb in new[] { _vertical, _horizontal, _grid }) rb.CheckedChanged += OnOptionChanged;
        foreach (var nud in new[] { _spacing, _margin, _borderWidth }) nud.ValueChanged += OnOptionChanged;
        foreach (var cb in new[] { _matchSize, _transparent, _border }) cb.CheckedChanged += OnOptionChanged;
        foreach (var combo in new[] { _align, _columns, _outputScale }) combo.SelectedIndexChanged += OnOptionChanged;
        _bgColor.Click += (_, _) => PickOptionColor(_bgColor, (o, c) => o.Background = c);
        _borderColor.Click += (_, _) => PickOptionColor(_borderColor, (o, c) => o.BorderColor = c);

        _canvas.AllowDrop = true;
        _canvas.DragEnter += OnExternalDragEnter;
        _canvas.DragDrop += OnExternalDragDrop;
        _canvas.DocumentChanged += (_, _) => OnDrawingsChanged();
        _canvas.ZoomChanged += (_, _) => SyncZoomUi();
        _canvas.SnipClicked += (_, i) => SelectSnip(i);

        var container = new Panel { Dock = DockStyle.Fill };
        container.Controls.Add(_canvas);
        container.Controls.Add(options);
        return container;
    }

    private static readonly int[] GridColumnChoices = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 15, 20 };
    private static readonly int[] OutputScales = { 100, 75, 50, 33, 25 };

    private static Label Lbl(string text) => new() { Text = text, AutoSize = true };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 0, 14, 2) };
        foreach (Control c in controls)
        {
            if (c is Label or CheckBox or RadioButton) c.Margin = new Padding(3, 6, 3, 3);
            row.Controls.Add(c);
        }
        return row;
    }

    private StatusStrip BuildStatusBar()
    {
        var bar = new StatusStrip();
        bar.Items.Add(_status);
        _hotkeyStatus.Click += (_, _) => ChangeHotkey();
        bar.Items.Add(_hotkeyStatus);
        bar.Items.Add(new ToolStripSeparator());

        var minus = new ToolStripButton("−") { ToolTipText = "Zoom out (Ctrl+-)" };
        minus.Click += (_, _) => _canvas.ZoomBy(0.8f);
        var plus = new ToolStripButton("+") { ToolTipText = "Zoom in (Ctrl++)" };
        plus.Click += (_, _) => _canvas.ZoomBy(1.25f);
        _zoomSlider.Height = LogicalToDeviceUnits(22);
        _zoomSlider.ValueChanged += (_, _) => { if (!_syncingZoom) _canvas.SetZoom(SliderToZoom(_zoomSlider.Value)); };
        _zoomCombo.Items.Add("Fit");
        foreach (int z in ZoomPresets) _zoomCombo.Items.Add($"{z}%");
        _zoomCombo.SelectedIndexChanged += (_, _) => { if (!_syncingZoom) ApplyZoomText(_zoomCombo.Text); };
        _zoomCombo.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { ApplyZoomText(_zoomCombo.Text); e.SuppressKeyPress = true; _canvas.Focus(); } };

        bar.Items.Add(minus);
        bar.Items.Add(new ToolStripControlHost(_zoomSlider) { AutoSize = false, Width = _zoomSlider.Width, ToolTipText = "Zoom" });
        bar.Items.Add(plus);
        bar.Items.Add(_zoomCombo);
        return bar;
    }

    // Slider is logarithmic (like Paint), so 100% sits near the middle.
    private static readonly double LogMin = Math.Log(EditorCanvas.MinZoom), LogMax = Math.Log(EditorCanvas.MaxZoom);
    private static float SliderToZoom(int v) => (float)Math.Exp(LogMin + (LogMax - LogMin) * v / 100.0);
    private static int ZoomToSlider(float z) => (int)Math.Round(Math.Clamp((Math.Log(z) - LogMin) / (LogMax - LogMin), 0, 1) * 100);

    private void ApplyZoomText(string text)
    {
        string t = text.Trim();
        if (t.StartsWith("Fit", StringComparison.OrdinalIgnoreCase)) { _canvas.SetZoom(null); return; }
        if (float.TryParse(t.TrimEnd('%'), out float pct) && pct > 0) _canvas.SetZoom(pct / 100f);
        else SyncZoomUi();
    }

    private void SyncZoomUi()
    {
        _syncingZoom = true;
        float z = _canvas.Zoom;
        _zoomSlider.Value = ZoomToSlider(z);
        _zoomCombo.Text = _canvas.IsFit ? $"Fit ({z * 100:0}%)" : $"{z * 100:0}%";
        _syncingZoom = false;
    }

    private void BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("New Snip", null, async (_, _) => await SnipAsync(SnipMode.Region));
        menu.Items.Add("Full Screen Snip", null, async (_, _) => await SnipAsync(SnipMode.FullScreen));
        menu.Items.Add("Scrolling Capture (whole page)", null, async (_, _) => await SnipAsync(SnipMode.Scrolling));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Change Hotkey…", null, (_, _) => { ShowAndActivate(); ChangeHotkey(); });
        menu.Items.Add("Show Multi Snip", null, (_, _) => ShowAndActivate());
        menu.Items.Add("Exit", null, (_, _) => Close());
        _tray.ContextMenuStrip = menu;
        _tray.Icon = new Icon(Icon!, SystemInformation.SmallIconSize);
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowAndActivate();
        _tray.BalloonTipClicked += (_, _) => ShowAndActivate();
    }

    // ------------------------------------------------------------------ drawing tool state

    private void LoadToolSettings()
    {
        _loadingUi = true;
        _delayCombo.SelectedIndex = _settings.DelaySeconds switch { 3 => 1, 5 => 2, 10 => 3, _ => 0 };
        _keepSnippingButton.Checked = _keepSnippingMenu.Checked = _settings.KeepSnipping;
        _scrollTopMenu.Checked = _settings.ScrollToTopFirst;
        _fillButton.Checked = _canvas.Filled = _settings.ToolFilled;
        _boldButton.Checked = _canvas.BoldText = _settings.ToolBold;
        _canvas.LineWidth = _settings.ToolWidth;
        _canvas.FontSizePx = _settings.ToolFontSize;
        _sizeCombo.SelectedItem = $"{_settings.ToolWidth:0} px";
        if (_sizeCombo.SelectedIndex < 0) _sizeCombo.SelectedIndex = 2;
        _fontCombo.SelectedItem = $"{_settings.ToolFontSize:0} px";
        if (_fontCombo.SelectedIndex < 0) _fontCombo.SelectedIndex = 5;
        _loadingUi = false;
        SetToolColor(Color.FromArgb(_settings.ToolArgb), apply: false);
        SetTool(DrawTool.Select);
    }

    private void SetTool(DrawTool tool)
    {
        _canvas.Tool = tool;
        foreach (var (t, b) in _toolButtons) b.Checked = t == tool;
        _canvas.Focus();
    }

    private void SetToolColor(Color c, bool apply = true)
    {
        _canvas.DrawColor = c;
        _settings.ToolArgb = c.ToArgb();
        _colorButton.Image?.Dispose();
        _colorButton.Image = Swatch(c, _drawBar.ImageScalingSize.Width, current: true);
        _colorButton.ToolTipText = $"Current color ({ColorName(c)}) — click for more colors";
        if (apply) _canvas.ApplyStyleToSelection(color: true);
    }

    private void PickToolColor()
    {
        using var dlg = new ColorDialog { Color = _canvas.DrawColor, FullOpen = true, CustomColors = _settings.CustomColors ?? Array.Empty<int>() };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _settings.CustomColors = dlg.CustomColors;
        SetToolColor(dlg.Color);
    }
}
