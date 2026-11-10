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
        foreach (SnipItem s in p.Snips)
        {
            using var ms = new MemoryStream();
            s.Image.Save(ms, ImageFormat.Png);
            doc.Snips.Add(new SnipRecord { Source = s.Source, Png = ms.ToArray(), Annotations = s.Annotations.Select(a => a.Clone()).ToList() });
        }
        return doc;
    }

    public static Project FromDocument(ProjectDocument doc)
    {
        var p = new Project(doc.Options) { NextBadgeNumber = Math.Max(1, doc.NextBadgeNumber) };
        foreach (SnipRecord r in doc.Snips)
        {
            using var ms = new MemoryStream(r.Png);
            using var img = Image.FromStream(ms);
            p.Snips.Add(new SnipItem(ImageUtil.ToArgb(img), r.Source, r.Annotations));
        }
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

    public static Bitmap Flatten(Bitmap image, Color background)
    {
        var flat = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(flat);
        g.Clear(background);
        g.DrawImageUnscaled(image, 0, 0);
        return flat;
    }
}
