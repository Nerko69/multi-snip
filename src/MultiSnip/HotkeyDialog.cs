namespace MultiSnip;

/// <summary>
/// Lets the user record a new global snip hotkey by pressing it.
/// <paramref name="tryApply"/> registers the candidate and returns an error message if Windows refuses it.
/// </summary>
public sealed class HotkeyDialog : Form
{
    private readonly TextBox _box = new() { ReadOnly = true, Width = 260, TextAlign = HorizontalAlignment.Center, BackColor = SystemColors.Window };
    private readonly Label _message = new() { AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = Color.Firebrick };
    private readonly Func<Keys, string?> _tryApply;

    public Keys Selected { get; private set; }

    public HotkeyDialog(Keys current, Func<Keys, string?> tryApply)
    {
        _tryApply = tryApply;
        Selected = current;

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Snip hotkey";
        Icon = AppInfo.LoadIcon();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14);

        var layout = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(380, 0),
            Text = "Click the box and press the key combination you want to use to start a snip from anywhere in Windows " +
                   "(for example Ctrl+Shift+S or Ctrl+Alt+Q). Backspace clears it, which turns the global hotkey off.",
        });
        _box.Margin = new Padding(3, 10, 3, 6);
        _box.Font = new Font(Font.FontFamily, 12f);
        layout.Controls.Add(_box);
        layout.Controls.Add(_message);

        var buttons = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 10, 0, 0) };
        var ok = new Button { Text = "OK", AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var reset = new Button { Text = $"Reset to {Hotkey.Format(Hotkey.Default)}", AutoSize = true };
        ok.Click += (_, _) => Confirm();
        reset.Click += (_, _) => SetCandidate(Hotkey.Default);
        buttons.Controls.Add(reset);
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons);
        Controls.Add(layout);

        CancelButton = cancel;
        SetCandidate(current);
        Shown += (_, _) => _box.Focus();
        // Released only modifiers without a key: go back to showing the current candidate.
        _box.KeyUp += (_, _) => { if (_box.Text.EndsWith("+…")) SetCandidate(Selected); };
    }

    private void SetCandidate(Keys keys)
    {
        Selected = keys;
        _box.Text = keys == Keys.None ? "None (global hotkey off)" : Hotkey.Format(keys);
        _message.Text = Hotkey.Validate(keys) ?? "";
    }

    private void Confirm()
    {
        string? error = Hotkey.Validate(Selected) ?? _tryApply(Selected);
        if (error != null)
        {
            _message.Text = error;
            _box.Focus();
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!_box.Focused) return base.ProcessCmdKey(ref msg, keyData);

        Keys code = keyData & Keys.KeyCode;
        Keys mods = keyData & Keys.Modifiers;
        // Plain Esc / Tab / Enter keep their normal dialog meaning.
        if (mods == Keys.None && code is Keys.Escape or Keys.Tab)
            return base.ProcessCmdKey(ref msg, keyData);
        if (mods == Keys.None && code == Keys.Enter)
        {
            Confirm();
            return true;
        }
        if (mods == Keys.None && code is Keys.Back or Keys.Delete)
        {
            SetCandidate(Keys.None);
            return true;
        }
        if (Hotkey.IsModifierKey(code))
        {
            _box.Text = Hotkey.Format(mods) + "+…"; // show modifiers while the user is still pressing
            return true;
        }
        SetCandidate(keyData);
        return true;
    }
}
