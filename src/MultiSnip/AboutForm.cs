namespace MultiSnip;

public sealed class AboutForm : Form
{
    public AboutForm()
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
                "Shortcuts\n" +
                "  Ctrl+N  New snip          Ctrl+Shift+N  Full-screen snip\n" +
                "  Ctrl+Alt+S  Snip from anywhere (global)\n" +
                "  Ctrl+V  Paste image       Ctrl+O  Open images\n" +
                "  Ctrl+C  Copy combined     Ctrl+S  Save combined\n" +
                "  Ctrl+Up/Down  Reorder     Delete  Remove snip\n" +
                "  In the snip overlay: Enter = whole screen, Esc = cancel/stop",
        };
        layout.Controls.Add(text, 1, 0);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right, AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        layout.Controls.Add(ok, 1, 1);
        AcceptButton = CancelButton = ok;
        Controls.Add(layout);
    }
}
