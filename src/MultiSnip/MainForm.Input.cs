namespace MultiSnip;

public sealed partial class MainForm
{
    // ------------------------------------------------------------------ window

    private void ShowAndActivate()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    // ------------------------------------------------------------------ global hotkey

    private string HotkeyText => Hotkey.Format(_settings.SnipHotkey);

    /// <summary>Registers <paramref name="keys"/> as the global hotkey. Returns false if Windows refuses it.</summary>
    private bool RegisterSnipHotkey(Keys keys)
    {
        UnregisterSnipHotkey();
        if (keys == Keys.None || !IsHandleCreated) return keys == Keys.None;
        _hotkeyRegistered = NativeMethods.RegisterHotKey(Handle, HotkeyId, Hotkey.Modifiers(keys), (uint)(keys & Keys.KeyCode));
        return _hotkeyRegistered;
    }

    private void UnregisterSnipHotkey()
    {
        if (_hotkeyRegistered && IsHandleCreated) NativeMethods.UnregisterHotKey(Handle, HotkeyId);
        _hotkeyRegistered = false;
    }

    private void ChangeHotkey()
    {
        Keys previous = _settings.SnipHotkey;
        UnregisterSnipHotkey(); // so pressing the current hotkey in the dialog doesn't start a snip
        using var dlg = new HotkeyDialog(previous, keys =>
            RegisterSnipHotkey(keys) ? null
                : $"{Hotkey.Format(keys)} is already used by Windows or another app. Please choose another combination.");
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _settings.SnipHotkey = dlg.Selected;
            _settings.Save();
            SetStatus(dlg.Selected == Keys.None ? "Global hotkey turned off." : $"Global hotkey set to {HotkeyText}.");
        }
        else
        {
            RegisterSnipHotkey(previous);
        }
        UpdateHotkeyTexts();
    }

    /// <summary>Refreshes every place that mentions the hotkey (status bar, tray tooltip, empty-tab hint).</summary>
    private void UpdateHotkeyTexts()
    {
        bool off = _settings.SnipHotkey == Keys.None;
        _hotkeyStatus.Text = off ? "Global hotkey: off"
            : _hotkeyRegistered ? $"Global hotkey: {HotkeyText}"
            : $"Global hotkey: {HotkeyText} (unavailable)";
        string tray = off ? "Multi Snip" : $"Multi Snip — {HotkeyText} to snip";
        _tray.Text = tray.Length > 63 ? tray[..63] : tray; // NotifyIcon text limit
        _canvas.EmptyText =
            "Click  ✂ New Snip  (Ctrl+N)  to capture part of the screen.\n\n" +
            (off ? "Set a global hotkey with  ⌨ Hotkey…  to snip from anywhere in Windows.\n\n"
                 : $"Press  {HotkeyText}  anywhere in Windows to snip, even when this window is minimized.\n\n") +
            "You can also paste (Ctrl+V) an image from another snipping tool, open files, or drag & drop images here.\n\n" +
            "Then combine them (Vertical / Horizontal / Grid), draw and label with the tools above, and Save or Copy.\n" +
            "Use tabs (Ctrl+T) to work on several projects at once.";
        _canvas.Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (Hotkey.Validate(_settings.SnipHotkey) != null) _settings.SnipHotkey = Hotkey.Default; // hand-edited settings
        RegisterSnipHotkey(_settings.SnipHotkey);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        FitTabStrip();
        UpdateHotkeyTexts();
        if (!_hotkeyRegistered && _settings.SnipHotkey != Keys.None)
            SetStatus($"Note: {HotkeyText} is used by another app, so the global hotkey is unavailable. Click ⌨ Hotkey… to pick another.");
        _canvas.Focus();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        BeginInvoke(FitTabStrip); // after the controls have been rescaled
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterSnipHotkey();
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam == (IntPtr)HotkeyId)
        {
            _ = SnipAsync(SnipMode.Region);
            return;
        }
        if (m.Msg == NativeMethods.WM_SHOWME && NativeMethods.WM_SHOWME != 0)
        {
            ShowAndActivate();
            return;
        }
        base.WndProc(ref m);
    }

    // ------------------------------------------------------------------ keyboard

    private static readonly Dictionary<Keys, DrawTool> ToolKeys = new()
    {
        [Keys.V] = DrawTool.Select, [Keys.P] = DrawTool.Pen, [Keys.M] = DrawTool.Marker, [Keys.E] = DrawTool.Eraser,
        [Keys.L] = DrawTool.Line, [Keys.A] = DrawTool.Arrow, [Keys.R] = DrawTool.Rectangle, [Keys.O] = DrawTool.Ellipse,
        [Keys.T] = DrawTool.Text, [Keys.C] = DrawTool.BadgeCircle, [Keys.Q] = DrawTool.BadgeSquare,
    };

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Control? focused = FocusedControl();
        bool editingText = focused is TextBoxBase or UpDownBase or ComboBox || focused?.Parent is UpDownBase;
        bool onWorkArea = focused == _canvas || focused == _list;

        switch (keyData)
        {
            case Keys.Control | Keys.N: _ = SnipAsync(SnipMode.Region); return true;
            case Keys.Control | Keys.Shift | Keys.N: _ = SnipAsync(SnipMode.FullScreen); return true;
            case Keys.Control | Keys.Shift | Keys.R: _ = SnipAsync(SnipMode.Scrolling); return true;
            case Keys.Control | Keys.T: NewTab(); return true;
            case Keys.Control | Keys.W: CloseTab(_project); return true;
            case Keys.Control | Keys.Tab: SwitchTab(+1); return true;
            case Keys.Control | Keys.Shift | Keys.Tab: SwitchTab(-1); return true;
            case Keys.Control | Keys.O: OpenFiles(); return true;
            case Keys.Control | Keys.S: Save(); return true;
            case Keys.F12: SaveAs(); return true;
            case Keys.Control | Keys.E: ExportImage(); return true;
            case Keys.Control | Keys.Z when !editingText: Undo(); return true;
            case Keys.Control | Keys.Y when !editingText: Redo(); return true;
            case Keys.Control | Keys.Shift | Keys.Z when !editingText: Redo(); return true;
            case Keys.Control | Keys.V when !editingText: PasteFromClipboard(); return true;
            case Keys.Control | Keys.C when !editingText: CopyCombined(); return true;
            case Keys.Delete when onWorkArea: DeleteSelected(); return true;
            case Keys.Control | Keys.Up when !editingText: MoveSelected(-1); return true;
            case Keys.Control | Keys.Down when !editingText: MoveSelected(1); return true;
            case Keys.Control | Keys.Oemplus or Keys.Control | Keys.Add: _canvas.ZoomBy(1.25f); return true;
            case Keys.Control | Keys.OemMinus or Keys.Control | Keys.Subtract: _canvas.ZoomBy(0.8f); return true;
            case Keys.Control | Keys.D0 or Keys.Control | Keys.NumPad0: _canvas.SetZoom(null); return true;
            case Keys.Control | Keys.D1 or Keys.Control | Keys.NumPad1: _canvas.SetZoom(1f); return true;
            case Keys.Escape when focused == _canvas && _canvas.Tool != DrawTool.Select: SetTool(DrawTool.Select); return true;
        }
        // Single-letter tool shortcuts, only while the canvas or the snip list has the focus.
        if (onWorkArea && ToolKeys.TryGetValue(keyData, out DrawTool tool))
        {
            SetTool(tool);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private Control? FocusedControl()
    {
        Control? c = ActiveControl;
        while (c is ContainerControl { ActiveControl: { } inner }) c = inner;
        return c;
    }

    // ------------------------------------------------------------------ lifetime

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Open tabs (saved or not) are kept and come back next time, so closing never loses work.
        SaveSession();
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
            _base?.Dispose();
        }
        base.Dispose(disposing);
    }
}
