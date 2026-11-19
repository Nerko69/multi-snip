using System.Drawing.Imaging;
using System.Text;

namespace MultiSnip;

/// <summary>Shows an image file's metadata (EXIF camera/date/GPS/software…, text blocks) and removes all or some of it.</summary>
public sealed class MetadataDialog : Form
{
    private static readonly Dictionary<int, string> TagNames = new()
    {
        [0x010E] = "Description", [0x010F] = "Camera make", [0x0110] = "Camera model", [0x0112] = "Orientation",
        [0x011A] = "X resolution", [0x011B] = "Y resolution", [0x0128] = "Resolution unit", [0x0131] = "Software",
        [0x0132] = "Date / time", [0x013B] = "Artist", [0x013C] = "Host computer", [0x8298] = "Copyright",
        [0x9003] = "Date taken", [0x9004] = "Date digitised", [0x829A] = "Exposure time", [0x829D] = "F-number",
        [0x8827] = "ISO", [0x920A] = "Focal length", [0x9209] = "Flash", [0xA002] = "Pixel width", [0xA003] = "Pixel height",
        [0xA420] = "Unique image ID", [0xA430] = "Camera owner", [0xA431] = "Camera serial number", [0xA434] = "Lens model",
        [0x9C9B] = "Windows title", [0x9C9C] = "Windows comment", [0x9C9D] = "Windows author", [0x9C9E] = "Windows keywords",
        [0x9C9F] = "Windows subject", [0x0001] = "GPS latitude ref", [0x0002] = "GPS latitude", [0x0003] = "GPS longitude ref",
        [0x0004] = "GPS longitude", [0x0005] = "GPS altitude ref", [0x0006] = "GPS altitude", [0x0007] = "GPS time",
        [0x001D] = "GPS date", [0x5090] = "Luminance table", [0x5091] = "Chrominance table", [0x0301] = "Gamma",
        [0x5110] = "Pixel units", [0x5111] = "Pixels per unit X", [0x5112] = "Pixels per unit Y", [0x8769] = "EXIF block",
        [0x8825] = "GPS block", [0x927C] = "Maker note", [0x9286] = "User comment",
    };

    private readonly ListView _list = new() { View = View.Details, CheckBoxes = true, FullRowSelect = true, Dock = DockStyle.Fill, ShowGroups = true };
    private readonly Label _fileLabel = new() { AutoSize = true, MaximumSize = new Size(700, 0) };
    private readonly CheckBox _overwrite = new() { Text = "Overwrite the original file (otherwise a \"-clean\" copy is saved next to it)", AutoSize = true };
    private readonly Button _removeAll = new() { Text = "Remove ALL metadata", AutoSize = true };
    private readonly Button _removeSelected = new() { Text = "Remove checked fields", AutoSize = true };
    private string? _path;

    public MetadataDialog(string? path = null)
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Image metadata";
        Icon = AppInfo.LoadIcon();
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(780, 560);
        MinimumSize = new Size(560, 380);
        ShowInTaskbar = false;
        Padding = new Padding(10);

        _list.Columns.Add("Field", 220);
        _list.Columns.Add("Value", 470);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var open = new Button { Text = "Open image…", AutoSize = true };
        open.Click += (_, _) => ChooseFile();
        top.Controls.Add(open);
        top.Controls.Add(_fileLabel);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        bottom.Controls.Add(_overwrite);
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(_removeAll);
        buttons.Controls.Add(_removeSelected);
        buttons.Controls.Add(close);
        bottom.Controls.Add(buttons);
        bottom.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(720, 0), ForeColor = SystemColors.GrayText,
            Text = "\"Remove ALL\" keeps the picture exactly as it is for JPEG and PNG (no re-compression). Removing single " +
                   "fields re-saves the image (JPEGs at high quality).",
        });

        _removeAll.Click += (_, _) => RemoveAll();
        _removeSelected.Click += (_, _) => RemoveChecked();
        CancelButton = close;

        Controls.Add(_list);
        Controls.Add(top);
        Controls.Add(bottom);
        if (path != null) LoadFile(path);
        else UpdateButtons();
    }

    private void ChooseFile()
    {
        using var dlg = new OpenFileDialog { Title = "Open an image to see its metadata", Filter = "Images|*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.gif;*.bmp|All files|*.*" };
        if (dlg.ShowDialog(this) == DialogResult.OK) LoadFile(dlg.FileName);
    }

    private void LoadFile(string path)
    {
        _path = path;
        _list.BeginUpdate();
        _list.Items.Clear();
        _list.Groups.Clear();
        var fields = new ListViewGroup("Fields (EXIF / image properties)");
        var blocks = new ListViewGroup("Metadata blocks in the file");
        _list.Groups.Add(fields);
        _list.Groups.Add(blocks);
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            using (var ms = new MemoryStream(bytes))
            using (var img = Image.FromStream(ms))
            {
                foreach (PropertyItem p in img.PropertyItems)
                {
                    string name = TagNames.TryGetValue(p.Id, out string? n) ? n : $"Tag 0x{p.Id:X4}";
                    _list.Items.Add(new ListViewItem(new[] { name, Describe(p) }, fields) { Tag = p.Id });
                }
            }
            foreach (MetadataTools.Block b in MetadataTools.ListBlocks(bytes))
                _list.Items.Add(new ListViewItem(new[] { b.Name, $"{b.Bytes:N0} bytes  {b.Preview}" }, blocks) { Tag = null });
            _fileLabel.Text = $"{path}\n{_list.Items.Count} item(s) found" + (_list.Items.Count == 0 ? " — this image has no metadata." : ".");
        }
        catch (Exception ex)
        {
            _fileLabel.Text = $"{path}\nCould not read this file: {ex.Message}";
            _path = null;
        }
        _list.EndUpdate();
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        _removeAll.Enabled = _path != null && _list.Items.Count > 0;
        _removeSelected.Enabled = _path != null && _list.Items.Cast<ListViewItem>().Any(i => i.Tag is int);
    }

    private static string Describe(PropertyItem p)
    {
        byte[] v = p.Value ?? Array.Empty<byte>();
        try
        {
            switch (p.Type)
            {
                case 2: return Encoding.ASCII.GetString(v).TrimEnd('\0');
                case 3: return string.Join(", ", Enumerable.Range(0, v.Length / 2).Take(8).Select(i => BitConverter.ToUInt16(v, i * 2)));
                case 4: return string.Join(", ", Enumerable.Range(0, v.Length / 4).Take(8).Select(i => BitConverter.ToUInt32(v, i * 4)));
                case 5:
                case 10:
                    return string.Join(", ", Enumerable.Range(0, v.Length / 8).Take(4).Select(i =>
                    {
                        long num = p.Type == 5 ? BitConverter.ToUInt32(v, i * 8) : BitConverter.ToInt32(v, i * 8);
                        long den = p.Type == 5 ? BitConverter.ToUInt32(v, i * 8 + 4) : BitConverter.ToInt32(v, i * 8 + 4);
                        return den == 0 ? num.ToString() : (num / (double)den).ToString("0.####");
                    }));
                case 1 when p.Id is >= 0x9C9B and <= 0x9C9F:
                    return Encoding.Unicode.GetString(v).TrimEnd('\0');
                default:
                    return $"({v.Length} bytes)";
            }
        }
        catch
        {
            return $"({v.Length} bytes)";
        }
    }

    private string TargetPath()
    {
        if (_overwrite.Checked) return _path!;
        string dir = Path.GetDirectoryName(_path!)!;
        return Path.Combine(dir, Path.GetFileNameWithoutExtension(_path) + "-clean" + Path.GetExtension(_path));
    }

    private void RemoveAll()
    {
        if (_path == null) return;
        try
        {
            byte[] bytes = File.ReadAllBytes(_path);
            byte[]? clean = MetadataTools.StripAll(bytes, out _);
            string target = TargetPath();
            if (clean != null)
            {
                // Lossless for JPEG/PNG. GDI+ fields are inside the removed blocks.
                ProjectFile.WriteAtomically(target, clean);
            }
            else
            {
                using var ms = new MemoryStream(bytes);
                using var img = Image.FromStream(ms);
                using Bitmap plain = ImageUtil.ToArgb(img); // a fresh bitmap carries no properties
                ImageUtil.SaveByExtension(plain, target + ".tmp" + Path.GetExtension(target), Color.White);
                File.Move(target + ".tmp" + Path.GetExtension(target), target, overwrite: true);
            }
            Done(target);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Multi Snip — could not remove metadata", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RemoveChecked()
    {
        if (_path == null) return;
        var ids = _list.CheckedItems.Cast<ListViewItem>().Where(i => i.Tag is int).Select(i => (int)i.Tag!).ToList();
        if (ids.Count == 0)
        {
            MessageBox.Show(this, "Tick the fields you want to remove first.", "Multi Snip", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            string target = TargetPath();
            byte[] bytes = File.ReadAllBytes(_path);
            using var ms = new MemoryStream(bytes);
            using var img = Image.FromStream(ms);
            foreach (int id in ids)
                if (img.PropertyIdList.Contains(id)) img.RemovePropertyItem(id);
            string tmp = target + ".tmp" + Path.GetExtension(target);
            if (img.RawFormat.Guid == ImageFormat.Jpeg.Guid)
            {
                ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using var p = new EncoderParameters(1);
                p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 95L);
                img.Save(tmp, codec, p);
            }
            else
            {
                img.Save(tmp, img.RawFormat);
            }
            File.Move(tmp, target, overwrite: true);
            Done(target);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Multi Snip — could not remove fields", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Done(string target)
    {
        MessageBox.Show(this, $"Saved:\n{target}", "Multi Snip — metadata removed", MessageBoxButtons.OK, MessageBoxIcon.Information);
        LoadFile(target);
    }
}
