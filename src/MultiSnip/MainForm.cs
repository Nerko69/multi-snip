using System.Drawing.Imaging;

namespace MultiSnip;

public sealed class SnipItem : IDisposable
{
    public SnipItem(Bitmap image, string source)
    {
        Image = image;
        Source = source;
    }

    public Bitmap Image { get; }
    public string Source { get; }

    /// <summary>Cached list thumbnail (owned by this item).</summary>
    public Bitmap? Thumbnail { get; set; }

    public void Dispose()
    {
        Image.Dispose();
        Thumbnail?.Dispose();
    }
}

public sealed class MainForm : Form
{
    private const int HotkeyId = 0x5A01;
    private const string HotkeyText = "Ctrl+Shift+S";
    private const string InternalDragFormat = "MultiSnip.ItemIndex";

    private readonly Settings _settings = Settings.Load();
    private readonly List<SnipItem> _snips = new();
    private Bitmap? _combined;
    private bool _combinedDirty = true;
    private bool _snipping;
    private bool _loadingUi;
    private bool _hotkeyRegistered;

    // UI
    private readonly ToolStrip _toolbar = new();
    private readonly ToolStripComboBox _delayCombo = new();
    private readonly ToolStripButton _keepSnippingButton = new("Keep snipping");
    private readonly ListView _list = new();
    private readonly ImageList _thumbs = new();
    private readonly RadioButton _vertical = new() { Text = "Vertical", AutoSize = true };
    private readonly RadioButton _horizontal = new() { Text = "Horizontal", AutoSize = true };
    private readonly RadioButton _grid = new() { Text = "Grid", AutoSize = true };
    private readonly NumericUpDown _columns = new() { Minimum = 1, Maximum = 20, Width = 50 };
    private readonly ComboBox _align = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly NumericUpDown _spacing = new() { Minimum = 0, Maximum = 500, Width = 60 };
    private readonly NumericUpDown _margin = new() { Minimum = 0, Maximum = 500, Width = 60 };
    private readonly CheckBox _matchSize = new() { Text = "Match sizes", AutoSize = true };
    private readonly Button _bgColor = new() { Width = 40, FlatStyle = FlatStyle.Flat };
    private readonly CheckBox _transparent = new() { Text = "Transparent", AutoSize = true };
    private readonly CheckBox _border = new() { Text = "Border", AutoSize = true };
    private readonly Button _borderColor = new() { Width = 40, FlatStyle = FlatStyle.Flat };
    private readonly NumericUpDown _borderWidth = new() { Minimum = 1, Maximum = 50, Width = 50 };
    private readonly CheckBox _fitPreview = new() { Text = "Fit preview", AutoSize = true };
    private readonly Panel _previewPanel = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(64, 64, 64) };
    private readonly PictureBox _preview = new() { BackColor = Color.Transparent };
    private readonly Label _emptyHint = new();
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly NotifyIcon _tray = new();
    private readonly System.Windows.Forms.Timer _renderTimer = new() { Interval = 120 };
    private Point _previewDragStart;

    public MainForm()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = $"Multi Snip v{AppInfo.Version}";
        Icon = AppInfo.LoadIcon();
        MinimumSize = new Size(760, 480);
        Size = new Size(1150, 760);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        AllowDrop = true;

        BuildToolbar();
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            SplitterWidth = 6,
        };
        split.Panel1.Controls.Add(BuildSnipList());
        split.Panel2.Controls.Add(BuildPreviewArea());

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_status);
        statusStrip.Items.Add(new ToolStripStatusLabel($"Global hotkey: {HotkeyText}") { ForeColor = SystemColors.GrayText });

        Controls.Add(split);
        Controls.Add(_toolbar);
        Controls.Add(statusStrip);
        BuildTray();
        ResumeLayout(true);
        split.SplitterDistance = LogicalToDeviceUnits(250);

        LoadSettingsIntoUi();
        _renderTimer.Tick += (_, _) => { _renderTimer.Stop(); RenderPreview(); };
        DragEnter += OnExternalDragEnter;
        DragDrop += OnExternalDragDrop;
        RefreshAll();
    }

    // ------------------------------------------------------------------ UI construction

    private void BuildToolbar()
    {
        _toolbar.GripStyle = ToolStripGripStyle.Hidden;
        _toolbar.Padding = new Padding(4, 2, 4, 2);
        _toolbar.ImageScalingSize = new Size(20, 20);

        _toolbar.Items.Add(Button("✂  New Snip", "Drag a rectangle to snip (Ctrl+N)", async () => await SnipAsync(false)));
        _toolbar.Items.Add(Button("🖥  Full Screen", "Snip the whole screen the mouse is on (Ctrl+Shift+N)", async () => await SnipAsync(true)));
        _toolbar.Items.Add(new ToolStripLabel("Delay:"));
        _delayCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _delayCombo.Items.AddRange(new object[] { "None", "3 sec", "5 sec", "10 sec" });
        _delayCombo.AutoSize = false;
        _delayCombo.Width = 70;
        _delayCombo.SelectedIndexChanged += (_, _) =>
        {
            _settings.DelaySeconds = _delayCombo.SelectedIndex switch { 1 => 3, 2 => 5, 3 => 10, _ => 0 };
        };
        _toolbar.Items.Add(_delayCombo);
        _keepSnippingButton.CheckOnClick = true;
        _keepSnippingButton.ToolTipText = "After each snip, immediately start the next one. Press Esc to stop.";
        _keepSnippingButton.CheckedChanged += (_, _) => _settings.KeepSnipping = _keepSnippingButton.Checked;
        _toolbar.Items.Add(_keepSnippingButton);
        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add(Button("📋  Paste", "Add the image on the clipboard (Ctrl+V)", PasteFromClipboard));
        _toolbar.Items.Add(Button("📂  Open…", "Add image files (Ctrl+O)", OpenFiles));
        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add(Button("⧉  Copy Combined", "Copy the combined image to the clipboard (Ctrl+C)", CopyCombined));
        _toolbar.Items.Add(Button("💾  Save As…", "Save the combined image (Ctrl+S)", SaveCombined));
        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add(Button("🗑  Clear All", "Remove all snips", ClearAll));

        var about = Button("About", "About Multi Snip", () => { using var f = new AboutForm(); f.ShowDialog(this); });
        about.Alignment = ToolStripItemAlignment.Right;
        _toolbar.Items.Add(about);
    }

    private static ToolStripButton Button(string text, string tip, Action onClick)
    {
        var b = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = tip };
        b.Click += (_, _) => onClick();
        return b;
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
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete) { RemoveSelected(); e.Handled = true; }
        };
        _list.ContextMenuStrip = BuildListMenu();
        panel.Controls.Add(_list, 0, 1);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 4, 0, 0) };
        buttons.Controls.Add(SmallButton("▲ Up", "Move selected up (Ctrl+Up)", () => MoveSelected(-1)));
        buttons.Controls.Add(SmallButton("▼ Down", "Move selected down (Ctrl+Down)", () => MoveSelected(1)));
        buttons.Controls.Add(SmallButton("✕ Remove", "Remove selected (Delete)", RemoveSelected));
        panel.Controls.Add(buttons, 0, 2);
        return panel;
    }

    private ContextMenuStrip BuildListMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Copy this snip", null, (_, _) => { if (Selected().FirstOrDefault() is { } s) SetClipboardImage(s.Image); });
        menu.Items.Add("Save this snip as…", null, (_, _) => { if (Selected().FirstOrDefault() is { } s) SaveImage(s.Image, "Snip"); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Move up", null, (_, _) => MoveSelected(-1));
        menu.Items.Add("Move down", null, (_, _) => MoveSelected(1));
        menu.Items.Add("Move to top", null, (_, _) => MoveSelectedTo(0));
        menu.Items.Add("Move to bottom", null, (_, _) => MoveSelectedTo(_snips.Count));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Remove", null, (_, _) => RemoveSelected());
        menu.Opening += (_, e) => e.Cancel = _list.SelectedIndices.Count == 0;
        return menu;
    }

    private Button SmallButton(string text, string tip, Action onClick)
    {
        var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(4, 0, 4, 0) };
        new ToolTip().SetToolTip(b, tip);
        b.Click += (_, _) => onClick();
        return b;
    }

    private Control BuildPreviewArea()
    {
        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Padding = new Padding(6, 6, 6, 2),
        };

        // Layout radio buttons share a container so they form one group.
        var layoutGroup = Row(new Label { Text = "Combine:", AutoSize = true, Anchor = AnchorStyles.Left, Font = new Font(Font, FontStyle.Bold) },
            _vertical, _horizontal, _grid, new Label { Text = "columns", AutoSize = true, Anchor = AnchorStyles.Left }, _columns);
        options.Controls.Add(layoutGroup);
        options.SetFlowBreak(layoutGroup, true);
        options.Controls.Add(Row(Lbl("Align"), _align));
        options.Controls.Add(Row(Lbl("Spacing"), _spacing));
        options.Controls.Add(Row(Lbl("Margin"), _margin));
        options.Controls.Add(Row(_matchSize));
        options.Controls.Add(Row(Lbl("Background"), _bgColor, _transparent));
        options.Controls.Add(Row(_border, _borderColor, _borderWidth));
        options.Controls.Add(Row(_fitPreview));

        var tips = new ToolTip();
        tips.SetToolTip(_matchSize, "Vertical: scale snips to the same width. Horizontal: same height. Grid: same cell size.");
        tips.SetToolTip(_spacing, "Gap between snips, in pixels");
        tips.SetToolTip(_margin, "Outer padding around the combined image, in pixels");
        tips.SetToolTip(_transparent, "Transparent background (kept when saving PNG)");
        tips.SetToolTip(_borderWidth, "Border width, in pixels");

        foreach (var rb in new[] { _vertical, _horizontal, _grid }) rb.CheckedChanged += OnOptionChanged;
        foreach (var nud in new[] { _columns, _spacing, _margin, _borderWidth }) nud.ValueChanged += OnOptionChanged;
        foreach (var cb in new[] { _matchSize, _transparent, _border }) cb.CheckedChanged += OnOptionChanged;
        _align.SelectedIndexChanged += OnOptionChanged;
        _fitPreview.CheckedChanged += (_, _) => { _settings.FitPreview = _fitPreview.Checked; ApplyPreviewMode(); };
        _bgColor.Click += (_, _) => PickColor(_bgColor, c => _settings.BackgroundArgb = c.ToArgb());
        _borderColor.Click += (_, _) => PickColor(_borderColor, c => _settings.BorderArgb = c.ToArgb());

        _preview.MouseDown += (_, e) => _previewDragStart = e.Location;
        _preview.MouseMove += OnPreviewMouseMove;
        _preview.DoubleClick += (_, _) => { _fitPreview.Checked = !_fitPreview.Checked; };
        new ToolTip().SetToolTip(_preview, "Drag this image into another app (chat, email, document) — or double-click to toggle zoom.");
        _previewPanel.Controls.Add(_preview);

        _emptyHint.Dock = DockStyle.Fill;
        _emptyHint.TextAlign = ContentAlignment.MiddleCenter;
        _emptyHint.ForeColor = Color.Gainsboro;
        _emptyHint.Font = new Font(Font.FontFamily, 12f);
        _emptyHint.Text =
            "Click  ✂ New Snip  (Ctrl+N)  to capture part of the screen.\n\n" +
            $"Press  {HotkeyText}  anywhere in Windows to snip, even when this window is minimized.\n\n" +
            "You can also paste (Ctrl+V), open, or drag & drop images here.\n\n" +
            "Then choose Vertical / Horizontal / Grid and Copy or Save the combined image.";
        _previewPanel.Controls.Add(_emptyHint);

        var container = new Panel { Dock = DockStyle.Fill };
        container.Controls.Add(_previewPanel);
        container.Controls.Add(options);
        return container;
    }

    private static Label Lbl(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0, 0, 14, 2),
        };
        foreach (Control c in controls)
        {
            if (c is Label or CheckBox or RadioButton) c.Margin = new Padding(3, 6, 3, 3);
            row.Controls.Add(c);
        }
        return row;
    }

    private void BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("New Snip", null, async (_, _) => await SnipAsync(false));
        menu.Items.Add("Full Screen Snip", null, async (_, _) => await SnipAsync(true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Show Multi Snip", null, (_, _) => ShowAndActivate());
        menu.Items.Add("Exit", null, (_, _) => Close());
        _tray.ContextMenuStrip = menu;
        _tray.Icon = new Icon(Icon!, SystemInformation.SmallIconSize);
        _tray.Text = $"Multi Snip — {HotkeyText} to snip";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowAndActivate();
        _tray.BalloonTipClicked += (_, _) => ShowAndActivate();
    }

    // ------------------------------------------------------------------ settings <-> UI

    private void LoadSettingsIntoUi()
    {
        _loadingUi = true;
        _vertical.Checked = _settings.Layout == CombineLayout.Vertical;
        _horizontal.Checked = _settings.Layout == CombineLayout.Horizontal;
        _grid.Checked = _settings.Layout == CombineLayout.Grid;
        _columns.Value = Math.Clamp(_settings.GridColumns, 1, 20);
        UpdateAlignLabels();
        _align.SelectedIndex = (int)_settings.Alignment;
        _spacing.Value = Math.Clamp(_settings.Spacing, 0, 500);
        _margin.Value = Math.Clamp(_settings.Margin, 0, 500);
        _matchSize.Checked = _settings.MatchSize;
        _transparent.Checked = _settings.TransparentBackground;
        _border.Checked = _settings.Border;
        _borderWidth.Value = Math.Clamp(_settings.BorderWidth, 1, 50);
        _bgColor.BackColor = Color.FromArgb(255, Color.FromArgb(_settings.BackgroundArgb));
        _borderColor.BackColor = Color.FromArgb(255, Color.FromArgb(_settings.BorderArgb));
        _fitPreview.Checked = _settings.FitPreview;
        _delayCombo.SelectedIndex = _settings.DelaySeconds switch { 3 => 1, 5 => 2, 10 => 3, _ => 0 };
        _keepSnippingButton.Checked = _settings.KeepSnipping;
        _loadingUi = false;
        UpdateOptionEnabledState();
        ApplyPreviewMode();
    }

    private void OnOptionChanged(object? sender, EventArgs e)
    {
        if (_loadingUi) return;
        if (sender is RadioButton { Checked: false }) return; // handle only the newly checked one
        _settings.Layout = _grid.Checked ? CombineLayout.Grid : _horizontal.Checked ? CombineLayout.Horizontal : CombineLayout.Vertical;
        _settings.GridColumns = (int)_columns.Value;
        _settings.Alignment = (CombineAlignment)Math.Max(0, _align.SelectedIndex);
        _settings.Spacing = (int)_spacing.Value;
        _settings.Margin = (int)_margin.Value;
        _settings.MatchSize = _matchSize.Checked;
        _settings.TransparentBackground = _transparent.Checked;
        _settings.Border = _border.Checked;
        _settings.BorderWidth = (int)_borderWidth.Value;
        if (sender is RadioButton) UpdateAlignLabels();
        UpdateOptionEnabledState();
        InvalidateCombined();
    }

    private void UpdateAlignLabels()
    {
        string[] labels = _settings.Layout switch
        {
            CombineLayout.Vertical => new[] { "Left", "Center", "Right" },
            CombineLayout.Horizontal => new[] { "Top", "Middle", "Bottom" },
            _ => new[] { "Top-left", "Center", "Bottom-right" },
        };
        bool wasLoading = _loadingUi;
        _loadingUi = true;
        int idx = _align.SelectedIndex;
        _align.Items.Clear();
        _align.Items.AddRange(labels);
        _align.SelectedIndex = idx < 0 ? 0 : idx;
        _loadingUi = wasLoading;
    }

    private void UpdateOptionEnabledState()
    {
        _columns.Enabled = _grid.Checked;
        _bgColor.Enabled = !_transparent.Checked;
        _borderColor.Enabled = _borderWidth.Enabled = _border.Checked;
    }

    private void PickColor(Button swatch, Action<Color> apply)
    {
        using var dlg = new ColorDialog { Color = swatch.BackColor, FullOpen = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        swatch.BackColor = dlg.Color;
        apply(dlg.Color);
        InvalidateCombined();
    }

    // ------------------------------------------------------------------ snipping

    private async Task SnipAsync(bool fullScreen)
    {
        if (_snipping) return;
        _snipping = true;
        bool windowWasVisible = Visible && WindowState != FormWindowState.Minimized;
        int added = 0;
        try
        {
            if (windowWasVisible) Opacity = 0; // get out of the way of the screenshot
            int delayMs = _settings.DelaySeconds * 1000;
            while (true)
            {
                // Give the window/menus time to disappear (plus the optional user delay).
                await Task.Delay(Math.Max(220, delayMs));
                delayMs = 0;

                Bitmap? result;
                using (Bitmap screen = SnipOverlayForm.CaptureVirtualScreen(out Rectangle virtualBounds))
                {
                    if (fullScreen)
                    {
                        Rectangle b = Screen.FromPoint(Cursor.Position).Bounds;
                        b.Offset(-virtualBounds.X, -virtualBounds.Y);
                        b.Intersect(new Rectangle(Point.Empty, screen.Size));
                        result = screen.Clone(b, PixelFormat.Format32bppArgb);
                    }
                    else
                    {
                        string hint = _settings.KeepSnipping
                            ? $"Snip #{_snips.Count + 1}: drag to select · Esc to finish"
                            : "Drag to select · Enter = whole screen · Esc to cancel";
                        using var overlay = new SnipOverlayForm(screen, virtualBounds, hint);
                        overlay.ShowDialog();
                        result = overlay.Result;
                    }
                }

                if (result == null) break;
                AddSnip(new SnipItem(result, fullScreen ? "Screen" : "Snip"));
                added++;
                if (fullScreen || !_settings.KeepSnipping) break;
            }
        }
        catch (Exception ex)
        {
            ShowError("Snipping failed", ex);
        }
        finally
        {
            _snipping = false;
            if (windowWasVisible)
            {
                Opacity = 1;
                Activate();
            }
            else if (added > 0)
            {
                _tray.ShowBalloonTip(2000, "Multi Snip",
                    $"{added} snip(s) added — {_snips.Count} in total. Click to combine.", ToolTipIcon.Info);
            }
        }
    }

    // ------------------------------------------------------------------ snip list management

    private void AddSnip(SnipItem item)
    {
        _snips.Add(item);
        RefreshAll(selectIndex: _snips.Count - 1);
    }

    private void AddImages(IEnumerable<Bitmap> images, string source)
    {
        int before = _snips.Count;
        foreach (Bitmap b in images) _snips.Add(new SnipItem(b, source));
        if (_snips.Count != before) RefreshAll(selectIndex: _snips.Count - 1);
    }

    private IEnumerable<SnipItem> Selected() =>
        _list.SelectedIndices.Cast<int>().OrderBy(i => i).Select(i => _snips[i]);

    private void MoveSelected(int delta)
    {
        var selected = _list.SelectedIndices.Cast<int>().OrderBy(i => i).ToList();
        if (selected.Count == 0) return;
        if (delta < 0 && selected[0] == 0) return;
        if (delta > 0 && selected[^1] == _snips.Count - 1) return;
        if (delta > 0) selected.Reverse();
        foreach (int i in selected)
            (_snips[i], _snips[i + delta]) = (_snips[i + delta], _snips[i]);
        RefreshAll(selected.Select(i => i + delta));
    }

    private void MoveSelectedTo(int target)
    {
        var selected = _list.SelectedIndices.Cast<int>().OrderBy(i => i).ToList();
        if (selected.Count == 0) return;
        var items = selected.Select(i => _snips[i]).ToList();
        int insertAt = target - selected.Count(i => i < target);
        foreach (int i in Enumerable.Reverse(selected)) _snips.RemoveAt(i);
        insertAt = Math.Clamp(insertAt, 0, _snips.Count);
        _snips.InsertRange(insertAt, items);
        RefreshAll(Enumerable.Range(insertAt, items.Count));
    }

    private void RemoveSelected()
    {
        var selected = _list.SelectedIndices.Cast<int>().OrderByDescending(i => i).ToList();
        if (selected.Count == 0) return;
        foreach (int i in selected)
        {
            _snips[i].Dispose();
            _snips.RemoveAt(i);
        }
        RefreshAll(selectIndex: Math.Min(selected[^1], _snips.Count - 1));
    }

    private void ClearAll()
    {
        if (_snips.Count == 0) return;
        if (MessageBox.Show(this, $"Remove all {_snips.Count} snips?", "Multi Snip",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        foreach (SnipItem s in _snips) s.Dispose();
        _snips.Clear();
        RefreshAll();
    }

    private void RefreshAll(int selectIndex) => RefreshAll(selectIndex >= 0 ? new[] { selectIndex } : Array.Empty<int>());

    private void RefreshAll(IEnumerable<int>? select = null)
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        _thumbs.Images.Clear();
        for (int i = 0; i < _snips.Count; i++)
        {
            _snips[i].Thumbnail ??= MakeThumbnail(_snips[i].Image, _thumbs.ImageSize);
            _thumbs.Images.Add(_snips[i].Thumbnail!);
            _list.Items.Add(new ListViewItem($"{i + 1}.  {_snips[i].Image.Width} × {_snips[i].Image.Height}", i));
        }
        if (select != null)
        {
            foreach (int i in select)
                if (i >= 0 && i < _list.Items.Count)
                {
                    _list.Items[i].Selected = true;
                    _list.Items[i].EnsureVisible();
                }
        }
        _list.EndUpdate();
        InvalidateCombined();
    }

    private static Bitmap MakeThumbnail(Image src, Size box)
    {
        var thumb = new Bitmap(box.Width, box.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(thumb);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        double k = Math.Min(1.0, Math.Min((box.Width - 2) / (double)src.Width, (box.Height - 2) / (double)src.Height));
        int w = Math.Max(1, (int)(src.Width * k)), h = Math.Max(1, (int)(src.Height * k));
        var dest = new Rectangle((box.Width - w) / 2, (box.Height - h) / 2, w, h);
        g.DrawImage(src, dest);
        g.DrawRectangle(Pens.Silver, dest.X - 1, dest.Y - 1, dest.Width + 1, dest.Height + 1);
        return thumb;
    }

    // ------------------------------------------------------------------ drag & drop

    private void OnListDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(InternalDragFormat) == true)
        {
            e.Effect = DragDropEffects.Move;
            Point pt = _list.PointToClient(new Point(e.X, e.Y));
            int idx = _list.InsertionMark.NearestIndex(pt);
            if (idx >= 0)
            {
                Rectangle r = _list.GetItemRect(idx);
                _list.InsertionMark.AppearsAfterItem = pt.Y > r.Bottom || (pt.Y >= r.Top && pt.X > r.Left + r.Width / 2);
            }
            _list.InsertionMark.Index = idx;
        }
        else
        {
            OnExternalDragEnter(sender, e);
        }
    }

    private void OnListDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(InternalDragFormat) is int from)
        {
            int mark = _list.InsertionMark.Index;
            bool after = _list.InsertionMark.AppearsAfterItem;
            _list.InsertionMark.Index = -1;
            if (mark < 0) return;
            int target = mark + (after ? 1 : 0);
            if (!_list.SelectedIndices.Contains(from))
            {
                _list.SelectedIndices.Clear();
                _list.SelectedIndices.Add(from);
            }
            MoveSelectedTo(target);
        }
        else
        {
            OnExternalDragDrop(sender, e);
        }
    }

    private void OnExternalDragEnter(object? sender, DragEventArgs e)
    {
        bool ok = e.Data != null && !e.Data.GetDataPresent(InternalDragFormat) &&
                  (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap));
        e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnExternalDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data == null || e.Data.GetDataPresent(InternalDragFormat)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            AddImages(LoadImageFiles(files), "File");
        else if (e.Data.GetData(DataFormats.Bitmap) is Image img)
            AddImages(new[] { ToArgb(img) }, "Dropped");
    }

    private void OnPreviewMouseMove(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _snips.Count == 0) return;
        Size drag = SystemInformation.DragSize;
        if (Math.Abs(e.X - _previewDragStart.X) < drag.Width && Math.Abs(e.Y - _previewDragStart.Y) < drag.Height) return;

        Bitmap combined = GetCombined();
        try
        {
            string dir = Path.Combine(Path.GetTempPath(), "MultiSnip");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, DefaultFileName() + ".png");
            combined.Save(file, ImageFormat.Png);
            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, new[] { file });
            using Bitmap flat = Flatten(combined, Color.White);
            data.SetData(DataFormats.Bitmap, flat);
            _preview.DoDragDrop(data, DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            ShowError("Could not start drag", ex);
        }
    }

    // ------------------------------------------------------------------ clipboard & files

    private void PasteFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                AddImages(LoadImageFiles(Clipboard.GetFileDropList().Cast<string>()), "File");
            }
            else if (Clipboard.ContainsImage() && Clipboard.GetImage() is Image img)
            {
                using (img) AddImages(new[] { ToArgb(img) }, "Pasted");
            }
            else
            {
                SetStatus("The clipboard does not contain an image.");
            }
        }
        catch (Exception ex)
        {
            ShowError("Paste failed", ex);
        }
    }

    private void OpenFiles()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Add images",
            Multiselect = true,
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            AddImages(LoadImageFiles(dlg.FileNames), "File");
    }

    private List<Bitmap> LoadImageFiles(IEnumerable<string> files)
    {
        var result = new List<Bitmap>();
        var failed = new List<string>();
        foreach (string f in files)
        {
            try
            {
                using var fs = File.OpenRead(f);
                using var img = Image.FromStream(fs);
                result.Add(ToArgb(img));
            }
            catch
            {
                failed.Add(Path.GetFileName(f));
            }
        }
        if (failed.Count > 0)
            SetStatus("Skipped (not an image): " + string.Join(", ", failed));
        return result;
    }

    /// <summary>Copies any image into a plain 32-bit bitmap (normalises DPI and pixel format, releases file locks).</summary>
    private static Bitmap ToArgb(Image img)
    {
        var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.DrawImage(img, new Rectangle(0, 0, img.Width, img.Height));
        return bmp;
    }

    private void CopyCombined()
    {
        if (_snips.Count == 0) { SetStatus("Nothing to copy yet — take a snip first."); return; }
        Bitmap c = GetCombined();
        SetClipboardImage(c);
        SetStatus($"Copied combined image ({c.Width} × {c.Height}) to the clipboard.");
    }

    private void SetClipboardImage(Bitmap image)
    {
        try
        {
            var data = new DataObject();
            // Plain bitmap for apps like Paint/Word, PNG (keeps transparency) for apps that support it.
            using Bitmap flat = Flatten(image, Color.White);
            data.SetData(DataFormats.Bitmap, true, flat);
            using var png = new MemoryStream();
            image.Save(png, ImageFormat.Png);
            data.SetData("PNG", false, png);
            Clipboard.SetDataObject(data, true, 5, 100); // copy=true renders all formats now
        }
        catch (Exception ex)
        {
            ShowError("Copy failed", ex);
        }
    }

    private void SaveCombined()
    {
        if (_snips.Count == 0) { SetStatus("Nothing to save yet — take a snip first."); return; }
        SaveImage(GetCombined(), DefaultFileName());
    }

    private void SaveImage(Bitmap image, string defaultName)
    {
        using var dlg = new SaveFileDialog
        {
            Title = "Save image",
            FileName = defaultName,
            Filter = "PNG image|*.png|JPEG image|*.jpg|Bitmap image|*.bmp|GIF image|*.gif",
            DefaultExt = "png",
            AddExtension = true,
        };
        if (!string.IsNullOrEmpty(_settings.LastSaveFolder) && Directory.Exists(_settings.LastSaveFolder))
            dlg.InitialDirectory = _settings.LastSaveFolder;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
            Color flattenColor = _settings.TransparentBackground ? Color.White : Color.FromArgb(255, Color.FromArgb(_settings.BackgroundArgb));
            switch (ext)
            {
                case ".jpg" or ".jpeg":
                {
                    using Bitmap flat = Flatten(image, flattenColor);
                    ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                    using var p = new EncoderParameters(1);
                    p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
                    flat.Save(dlg.FileName, codec, p);
                    break;
                }
                case ".bmp":
                {
                    using Bitmap flat = Flatten(image, flattenColor);
                    flat.Save(dlg.FileName, ImageFormat.Bmp);
                    break;
                }
                case ".gif":
                    image.Save(dlg.FileName, ImageFormat.Gif);
                    break;
                default:
                    image.Save(dlg.FileName, ImageFormat.Png);
                    break;
            }
            _settings.LastSaveFolder = Path.GetDirectoryName(dlg.FileName);
            SetStatus($"Saved {dlg.FileName}");
        }
        catch (Exception ex)
        {
            ShowError("Save failed", ex);
        }
    }

    private static Bitmap Flatten(Bitmap image, Color background)
    {
        var flat = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(flat);
        g.Clear(background);
        g.DrawImageUnscaled(image, 0, 0);
        return flat;
    }

    private static string DefaultFileName() => $"MultiSnip_{DateTime.Now:yyyyMMdd_HHmmss}";

    // ------------------------------------------------------------------ preview

    private void InvalidateCombined()
    {
        _combinedDirty = true;
        _renderTimer.Stop();
        _renderTimer.Start();
    }

    private Bitmap GetCombined()
    {
        if (_combinedDirty || _combined == null) RenderPreview();
        return _combined!;
    }

    private void RenderPreview()
    {
        _renderTimer.Stop();
        Bitmap? old = _combined;
        _combined = _snips.Count > 0 ? ImageCombiner.Combine(_snips.Select(s => (Image)s.Image).ToList(), _settings.ToCombineOptions()) : null;
        _combinedDirty = false;
        _preview.Image = _combined;
        old?.Dispose();

        bool empty = _combined == null;
        _emptyHint.Visible = empty;
        _preview.Visible = !empty;
        ApplyPreviewMode();
        SetStatus(empty
            ? "No snips yet."
            : $"{_snips.Count} snip(s) · combined image {_combined!.Width} × {_combined.Height} px");
    }

    private void ApplyPreviewMode()
    {
        if (_fitPreview.Checked)
        {
            _preview.SizeMode = PictureBoxSizeMode.Zoom;
            _preview.Dock = DockStyle.Fill;
        }
        else
        {
            _preview.Dock = DockStyle.None;
            _preview.SizeMode = PictureBoxSizeMode.AutoSize;
            _preview.Location = new Point(_previewPanel.AutoScrollPosition.X, _previewPanel.AutoScrollPosition.Y);
        }
        _preview.Cursor = Cursors.Hand;
    }

    private void SetStatus(string text) => _status.Text = text;

    private void ShowError(string title, Exception ex) =>
        MessageBox.Show(this, ex.Message, $"Multi Snip — {title}", MessageBoxButtons.OK, MessageBoxIcon.Error);

    // ------------------------------------------------------------------ window, hotkey, keyboard

    private void ShowAndActivate()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _hotkeyRegistered = NativeMethods.RegisterHotKey(Handle, HotkeyId,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT, (uint)Keys.S);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!_hotkeyRegistered)
            SetStatus($"Note: {HotkeyText} is used by another app, so the global hotkey is unavailable. Use the New Snip button.");
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_hotkeyRegistered) NativeMethods.UnregisterHotKey(Handle, HotkeyId);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam == (IntPtr)HotkeyId)
        {
            _ = SnipAsync(false);
            return;
        }
        if (m.Msg == NativeMethods.WM_SHOWME && NativeMethods.WM_SHOWME != 0)
        {
            ShowAndActivate();
            return;
        }
        base.WndProc(ref m);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        bool editingText = FocusedControl() is TextBoxBase or UpDownBase || FocusedControl()?.Parent is UpDownBase;
        switch (keyData)
        {
            case Keys.Control | Keys.N: _ = SnipAsync(false); return true;
            case Keys.Control | Keys.Shift | Keys.N: _ = SnipAsync(true); return true;
            case Keys.Control | Keys.O: OpenFiles(); return true;
            case Keys.Control | Keys.S: SaveCombined(); return true;
            case Keys.Control | Keys.V when !editingText: PasteFromClipboard(); return true;
            case Keys.Control | Keys.C when !editingText: CopyCombined(); return true;
            case Keys.Control | Keys.Up: MoveSelected(-1); return true;
            case Keys.Control | Keys.Down: MoveSelected(1); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private Control? FocusedControl()
    {
        Control? c = ActiveControl;
        while (c is ContainerControl { ActiveControl: { } inner }) c = inner;
        return c;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _settings.Save();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _renderTimer.Dispose();
            _combined?.Dispose();
            foreach (SnipItem s in _snips) s.Dispose();
        }
        base.Dispose(disposing);
    }
}
