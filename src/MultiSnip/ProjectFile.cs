using System.Drawing.Imaging;

namespace MultiSnip;

/// <summary>Saves a tab as an editable PNG (combined image + embedded project) and opens it again.</summary>
public static class ProjectFile
{
    public static ProjectDocument ToDocument(Project p)
    {
        var doc = new ProjectDocument
        {
            AppVersion = AppInfo.Version,
            Options = p.Options.Clone(),
            NextBadgeNumber = p.NextBadgeNumber,
            Title = p.DisplayName,
            FilePath = p.FilePath,
            Dirty = p.Dirty,
        };
        doc.Snips.AddRange(p.Snips.Select(ToRecord));
        return doc;
    }

    private static SnipRecord ToRecord(SnipItem s)
    {
        using var ms = new MemoryStream();
        s.Image.Save(ms, ImageFormat.Png);
        return new SnipRecord
        {
            Source = s.Source,
            Png = ms.ToArray(),
            Annotations = s.Annotations.Select(a => a.Clone()).ToList(),
            Children = s.Children?.Select(ToRecord).ToList(),
            GroupOptions = s.GroupOptions?.Clone(),
            OverlayBox = s.OverlayRect is { } r ? new[] { r.X, r.Y, r.Width, r.Height } : null,
        };
    }

    private static SnipItem FromRecord(SnipRecord r)
    {
        using var ms = new MemoryStream(r.Png);
        using var img = Image.FromStream(ms);
        RectangleF? overlay = r.OverlayBox is { Length: 4 } b ? new RectangleF(b[0], b[1], b[2], b[3]) : null;
        return new SnipItem(ImageUtil.ToArgb(img), r.Source, r.Annotations, r.Children?.Select(FromRecord).ToList(), r.GroupOptions, overlay);
    }

    public static Project FromDocument(ProjectDocument doc)
    {
        var p = new Project(doc.Options) { NextBadgeNumber = Math.Max(1, doc.NextBadgeNumber) };
        p.Snips.AddRange(doc.Snips.Select(FromRecord));
        return p;
    }

    /// <summary>Writes the editable PNG. Written to a temp file first, so a failed save never destroys the old file.</summary>
    public static void Save(Project p, string path)
    {
        byte[] png;
        using (Bitmap final = p.RenderFinal())
        using (var ms = new MemoryStream())
        {
            final.Save(ms, ImageFormat.Png);
            png = ms.ToArray();
        }
        byte[] file = EditablePng.Embed(png, ProjectArchive.Write(ToDocument(p)));
        WriteAtomically(path, file);
    }

    /// <summary>Returns the project stored in a Multi Snip PNG, or null for an ordinary image.</summary>
    public static Project? TryOpen(string path)
    {
        byte[]? data = EditablePng.TryExtract(File.ReadAllBytes(path));
        if (data == null) return null;
        Project p = FromDocument(ProjectArchive.Read(data));
        p.FilePath = path;
        return p;
    }

    public static void WriteAtomically(string path, byte[] bytes)
    {
        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }
}

public static class ImageUtil
{
    /// <summary>Copies any image into a plain 32-bit bitmap (normalises DPI and pixel format, releases file locks).</summary>
    public static Bitmap ToArgb(Image img)
    {
        var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.DrawImage(img, new Rectangle(0, 0, img.Width, img.Height));
        return bmp;
    }

    /// <summary>Saves as PNG / JPG (quality 92) / BMP / GIF / TIFF depending on the file extension.</summary>
    public static void SaveByExtension(Bitmap image, string path, Color flattenColor)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".jpg" or ".jpeg":
            {
                using Bitmap flat = Flatten(image, flattenColor);
                ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using var p = new EncoderParameters(1);
                p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
                flat.Save(path, codec, p);
                break;
            }
            case ".bmp":
            {
                using Bitmap flat = Flatten(image, flattenColor);
                flat.Save(path, ImageFormat.Bmp);
                break;
            }
            case ".gif":
                image.Save(path, ImageFormat.Gif);
                break;
            case ".tif" or ".tiff":
                image.Save(path, ImageFormat.Tiff);
                break;
            default:
                image.Save(path, ImageFormat.Png);
                break;
        }
    }

    public static Bitmap Flatten(Bitmap image, Color background)
    {
        var flat = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(flat);
        g.Clear(background);
        g.DrawImage(image, 0, 0, image.Width, image.Height);
        return flat;
    }
}
