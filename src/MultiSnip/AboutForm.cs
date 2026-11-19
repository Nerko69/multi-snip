namespace MultiSnip;

/// <summary>About box. The text (including all keyboard shortcuts) can be selected, or copied with one click.</summary>
public sealed class AboutForm : Form
{
    public static string BuildText(string snipHotkey) =>
        $"{AppInfo.Product}\r\n" +
        $"{AppInfo.Description}\r\n\r\n" +
        $"Version {AppInfo.Version}  (file version {AppInfo.FileVersion})\r\n" +
        $"Developer / idea & IP initiator: {AppInfo.Company}\r\n" +
        $"{AppInfo.Copyright}\r\n\r\n" +
        "CAPTURE\r\n" +
        "  Ctrl+N            New snip (drag a rectangle, or click a window/control)\r\n" +
        "  Ctrl+Shift+N      Full-screen snip\r\n" +
        "  Ctrl+Shift+R      Whole page: click the page and it scrolls and captures all of it (Esc stops)\r\n" +
        $"  {snipHotkey,-17} Snip from anywhere in Windows (change via Tools > Global Hotkey)\r\n" +
        "  In the snip overlay: Enter = whole screen, Esc = cancel / stop\r\n\r\n" +
        "TABS AND FILES\r\n" +
        "  Ctrl+T / Ctrl+W   New tab / close tab        Ctrl+Tab  Next tab\r\n" +
        "  Ctrl+O            Open (Multi Snip PNGs open with all snips editable)\r\n" +
        "  Ctrl+S / F12      Save / Save as             Ctrl+E    Export plain image\r\n" +
        "  Ctrl+Shift+E      Save each snip separately (Name-01, Name-02, ...)\r\n\r\n" +
        "EDITING\r\n" +
        "  Ctrl+Z / Ctrl+Y   Undo / redo\r\n" +
        "  Ctrl+V            Paste image as the next snip\r\n" +
        "  Ctrl+C            Copy the combined image\r\n" +
        "  Delete            Remove selected drawing / erase selected area / remove snip\r\n" +
        "  Enter             Crop to the selected area (Area tool)\r\n" +
        "  Ctrl+G            Keep as one (group snips)  Ctrl+Shift+G  Ungroup\r\n" +
        "  Ctrl+Up/Down      Reorder snips              Arrow keys    Nudge a drawing\r\n\r\n" +
        "TOOLS (click the image first)\r\n" +
        "  V Select   S Area (crop / erase)   P Pen   M Marker   E Eraser\r\n" +
        "  L Line   A Arrow   R Rectangle   O Ellipse   T Text   C Round label   Q Square label\r\n" +
        "  Shift while drawing = straight lines / squares / circles;  Esc = back to Select\r\n" +
        "  Overlay mode: Select tool drags snips; corner handles resize (Shift = free shape)\r\n\r\n" +
        "VIEW\r\n" +
        "  Ctrl + mouse wheel, Ctrl+Plus / Ctrl+Minus   Zoom\r\n" +
        "  Ctrl+0 Fit    Ctrl+1 100%    Middle mouse drag = pan\r\n" +
        "  View > Text Size: Normal / Large / Extra large\r\n";

    public AboutForm(string snipHotkey)
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "About Multi Snip";
        Icon = AppInfo.LoadIcon();
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        Size = new Size(820, 640);
        MinimumSize = new Size(520, 360);
        Padding = new Padding(12);

        string content = BuildText(snipHotkey);
        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
        header.Controls.Add(new PictureBox
        {
            Image = new Icon(AppInfo.LoadIcon(), 64, 64).ToBitmap(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(56, 56),
            Margin = new Padding(0, 0, 12, 8),
        });
        header.Controls.Add(new Label
        {
            Text = $"{AppInfo.Product}\nVersion {AppInfo.Version}",
            AutoSize = true,
            Font = new Font(Font.FontFamily, Font.Size + 3, FontStyle.Bold),
            Margin = new Padding(0, 6, 0, 0),
        });

        var text = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", Font.Size + 1),
            Text = content,
            BackColor = SystemColors.Window,
        };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var copy = new Button { Text = "📋 Copy all text", AutoSize = true };
        var copied = new Label { AutoSize = true, ForeColor = Color.ForestGreen, Margin = new Padding(0, 8, 8, 0) };
        copy.Click += (_, _) =>
        {
            Clipboard.SetText(content);
            copied.Text = "Copied — paste it into Notepad or any document.";
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(copy);
        buttons.Controls.Add(copied);

        Controls.Add(text);
        Controls.Add(header);
        Controls.Add(buttons);
        AcceptButton = CancelButton = ok;
        Shown += (_, _) => { text.SelectionStart = 0; text.SelectionLength = 0; ok.Focus(); };
    }
}
