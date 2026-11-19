using System.Drawing.Imaging;

namespace MultiSnip;

/// <summary>Watermark settings (text or image), with a live preview and bulk watermarking of a folder.</summary>
public sealed class WatermarkDialog : Form
{
    private readonly CheckBox _enabled = new() { Text = "Add a watermark to the finished image", AutoSize = true };
    private readonly RadioButton _text = new() { Text = "Text", AutoSize = true, Checked = true };
    private readonly RadioButton _image = new() { Text = "Image (logo)", AutoSize = true };
    private readonly TextBox _textBox = new() { Width = 300 };
    private readonly Button _color = new() { Width = 46, FlatStyle = FlatStyle.Flat };
    private readonly CheckBox _outline = new() { Text = "Outline (readable on any background)", AutoSize = true };
    private readonly Button _chooseImage = new() { Text = "Choose image…", AutoSize = true };
    private readonly Label _imageInfo = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly TrackBar _opacity = new() { Minimum = 5, Maximum = 100, TickFrequency = 5, Width = 220, SmallChange = 5, LargeChange = 10 };
    private readonly Label _opacityValue = new() { AutoSize = true };
    private readonly NumericUpDown _size = new() { Minimum = 3, Maximum = 100, Width = 60 };
    private readonly ComboBox _position = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly PictureBox _preview = new() { Size = new Size(360, 220), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
    private readonly Bitmap _sample;
    private readonly WatermarkOptions _w;
    private readonly Func<int[]?> _getCustomColors;
    private readonly Action<int[]> _setCustomColors;

    public WatermarkOptions Result => _w;

    public WatermarkDialog(WatermarkOptions current, Bitmap? sample, Func<int[]?> getCustomColors, Action<int[]> setCustomColors)
    {
        _w = current.Clone();
        _getCustomColors = getCustomColors;
        _setCustomColors = setCustomColors;
        _sample = sample ?? MakeSample();

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Watermark";
        Icon = AppInfo.LoadIcon();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14);

        foreach (WatermarkPosition p in Enum.GetValues<WatermarkPosition>())
            _position.Items.Add(p == WatermarkPosition.Tiled ? "Tiled (repeated, diagonal)" : SplitWords(p.ToString()));

        var form = new TableLayoutPanel { AutoSize = true, ColumnCount = 2 };
        void Row(string label, params Control[] cs)
        {
            var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            foreach (Control c in cs) flow.Controls.Add(c);
            form.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 7, 10, 0) });
            form.Controls.Add(flow);
        }
        form.Controls.Add(_enabled);
        form.SetColumnSpan(_enabled, 2);
        Row("Type", _text, _image);
        Row("Text", _textBox, _color);
        Row("", _outline);
        Row("Image", _chooseImage, _imageInfo);
        Row("Opacity", _opacity, _opacityValue);
        Row("Size", _size, new Label { Text = "% of the image width", AutoSize = true, Margin = new Padding(3, 7, 0, 0) });
        Row("Position", _position);
        form.Controls.Add(new Label { Text = "Preview", AutoSize = true, Margin = new Padding(0, 10, 10, 0) });
        form.Controls.Add(_preview);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 12, 0, 0) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var bulk = new Button { Text = "Watermark a whole folder…", AutoSize = true };
        bulk.Click += (_, _) => BulkWatermark(this, _w);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        buttons.Controls.Add(bulk);

        var outer = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        outer.Controls.Add(form);
        outer.Controls.Add(buttons);
        Controls.Add(outer);
        AcceptButton = ok;
        CancelButton = cancel;

        // load values
        _enabled.Checked = _w.Enabled;
        _text.Checked = _w.Kind == WatermarkKind.Text;
        _image.Checked = _w.Kind == WatermarkKind.Image;
        _textBox.Text = _w.Text;
        _color.BackColor = Color.FromArgb(255, _w.Color);
        _outline.Checked = _w.Outline;
        _opacity.Value = Math.Clamp(_w.Opacity, 5, 100);
        _size.Value = Math.Clamp(_w.SizePercent, 3, 100);
        _position.SelectedIndex = (int)_w.Position;
        UpdateImageInfo();

        // events
        _enabled.CheckedChanged += (_, _) => { _w.Enabled = _enabled.Checked; Changed(); };
        _text.CheckedChanged += (_, _) => { if (_text.Checked) { _w.Kind = WatermarkKind.Text; Changed(); } };
        _image.CheckedChanged += (_, _) => { if (_image.Checked) { _w.Kind = WatermarkKind.Image; Changed(); } };
        _textBox.TextChanged += (_, _) => { _w.Text = _textBox.Text; Changed(); };
        _outline.CheckedChanged += (_, _) => { _w.Outline = _outline.Checked; Changed(); };
        _opacity.ValueChanged += (_, _) => { _w.Opacity = _opacity.Value; Changed(); };
        _size.ValueChanged += (_, _) => { _w.SizePercent = (int)_size.Value; Changed(); };
        _position.SelectedIndexChanged += (_, _) => { _w.Position = (WatermarkPosition)_position.SelectedIndex; Changed(); };
        _color.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = _color.BackColor, FullOpen = true, CustomColors = _getCustomColors() ?? Array.Empty<int>() };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            _setCustomColors(dlg.CustomColors);
            _color.BackColor = dlg.Color;
            _w.Color = dlg.Color;
            Changed();
        };
        _chooseImage.Click += (_, _) => ChooseImage();
        Changed();
    }

    private static string SplitWords(string s) => string.Concat(s.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLower(c) : c.ToString()));

    private void ChooseImage()
    {
        using var dlg = new OpenFileDialog { Title = "Choose a watermark image (a PNG with transparency works best)", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            using var img = Image.FromFile(dlg.FileName);
            using Bitmap argb = ImageUtil.ToArgb(img);
            using var ms = new MemoryStream();
            argb.Save(ms, ImageFormat.Png);
            _w.ImagePng = ms.ToArray();
            _image.Checked = true;
            UpdateImageInfo();
            Changed();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Multi Snip — not an image", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateImageInfo()
    {
        if (_w.ImagePng == null) { _imageInfo.Text = "(none chosen)"; return; }
        using var ms = new MemoryStream(_w.ImagePng);
        using var img = Image.FromStream(ms);
        _imageInfo.Text = $"{img.Width} × {img.Height} px";
    }

    private void Changed()
    {
        bool text = _w.Kind == WatermarkKind.Text;
        _textBox.Enabled = _color.Enabled = _outline.Enabled = text;
        _chooseImage.Enabled = !text;
        _opacityValue.Text = $"{_w.Opacity}%";
        var preview = new Bitmap(_sample);
        var shown = _w.Clone();
        shown.Enabled = true; // always show what it will look like
        using (Graphics g = Graphics.FromImage(preview)) WatermarkRenderer.Draw(g, preview.Size, shown);
        _preview.Image?.Dispose();
        _preview.Image = preview;
    }

    private static Bitmap MakeSample()
    {
        var b = new Bitmap(800, 500);
        using Graphics g = Graphics.FromImage(b);
        using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 800, 500), Color.SteelBlue, Color.Bisque, 35f);
        g.FillRectangle(brush, 0, 0, 800, 500);
        return b;
    }

    /// <summary>Asks for a folder and writes watermarked copies of all its images into a "watermarked" sub-folder.</summary>
    public static void BulkWatermark(IWin32Window owner, WatermarkOptions w)
    {
        var mark = w.Clone();
        mark.Enabled = true;
        if (!mark.IsUsable)
        {
            MessageBox.Show(owner, "Set up the watermark first (text, or an image).", "Multi Snip", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dlg = new FolderBrowserDialog { Description = "Watermark all images in this folder. Copies go into a \"watermarked\" sub-folder; the originals are not changed.", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return;
        string output = Path.Combine(dlg.SelectedPath, "watermarked");
        var errors = new List<string>();
        Cursor.Current = Cursors.WaitCursor;
        int done = WatermarkRenderer.ApplyToFolder(dlg.SelectedPath, output, mark, errors);
        Cursor.Current = Cursors.Default;
        string msg = $"Watermarked {done} image(s) into:\n{output}";
        if (errors.Count > 0) msg += $"\n\n{errors.Count} file(s) could not be processed:\n" + string.Join("\n", errors.Take(10));
        if (MessageBox.Show(owner, msg + "\n\nOpen the folder now?", "Multi Snip — bulk watermark", MessageBoxButtons.YesNo,
                errors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information) == DialogResult.Yes && Directory.Exists(output))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{output}\"") { UseShellExecute = true });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _preview.Image?.Dispose();
            _sample.Dispose();
        }
        base.Dispose(disposing);
    }
}
