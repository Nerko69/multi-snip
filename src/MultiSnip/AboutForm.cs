namespace MultiSnip;

public sealed class AboutForm : Form
{
    public AboutForm(string snipHotkey)
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "About Multi Snip";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        var icon = new PictureBox
        {
            Image = new Icon(AppInfo.LoadIcon(), 64, 64).ToBitmap(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(64, 64),
            Margin = new Padding(0, 0, 16, 0),
        };
        layout.Controls.Add(icon, 0, 0);
        layout.SetRowSpan(icon, 2);

        var text = new Label
        {
            AutoSize = true,
            Text =
                $"{AppInfo.Product}\n" +
                $"{AppInfo.Description}\n\n" +
                $"Version {AppInfo.Version}  (file version {AppInfo.FileVersion})\n" +
                $"Developer / idea & IP initiator: {AppInfo.Company}\n" +
                $"{AppInfo.Copyright}\n\n" +
                "Capture\n" +
                "  Ctrl+N  New snip          Ctrl+Shift+N  Full-screen snip\n" +
                "  Ctrl+Shift+R  Scrolling capture (whole page; Esc stops)\n" +
                $"  {snipHotkey}  Snip from anywhere (global, change via ⌨ Hotkey…)\n" +
                "Tabs and files\n" +
                "  Ctrl+T  New tab   Ctrl+W  Close tab   Ctrl+Tab  Next tab\n" +
                "  Ctrl+O  Open      Ctrl+S  Save        F12  Save as   Ctrl+E  Export\n" +
                "Editing\n" +
                "  Ctrl+Z / Ctrl+Y  Undo / redo     Delete  Remove drawing or snip\n" +
                "  Ctrl+V  Paste as next snip       Ctrl+C  Copy combined image\n" +
                "  Ctrl+Up/Down  Reorder snips      Arrow keys  Nudge selected drawing\n" +
                "Tools (click the canvas first)\n" +
                "  V Select  P Pen  M Marker  E Eraser  L Line  A Arrow\n" +
                "  R Rectangle  O Ellipse  T Text  C Round label  Q Square label\n" +
                "  Shift while drawing = straight / square;  Esc = back to Select\n" +
                "Zoom\n" +
                "  Ctrl + mouse wheel, Ctrl+Plus/Minus, Ctrl+0 = fit, Ctrl+1 = 100%\n" +
                "  Middle mouse button drag = pan\n" +
                "  In the snip overlay: Enter = whole screen, Esc = cancel/stop",
        };
        layout.Controls.Add(text, 1, 0);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right, AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        layout.Controls.Add(ok, 1, 1);
        AcceptButton = CancelButton = ok;
        Controls.Add(layout);
    }
}
