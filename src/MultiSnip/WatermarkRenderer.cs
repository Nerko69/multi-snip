using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MultiSnip;

/// <summary>Draws text or image watermarks, and watermarks whole folders of images.</summary>
public static class WatermarkRenderer
{
    public static void Draw(Graphics g, Size image, WatermarkOptions w)
    {
        if (!w.IsUsable) return;
        GraphicsState state = g.Save();
        try
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (w.Kind == WatermarkKind.Image) DrawImageMark(g, image, w);
            else DrawTextMark(g, image, w);
        }
        finally
        {
            g.Restore(state);
        }
    }

    private static void Rotated(Graphics g, RectangleF r, bool tiled, Action<RectangleF> draw)
    {
        if (!tiled)
        {
            draw(r);
            return;
        }
        GraphicsState s = g.Save();
        g.TranslateTransform(r.X + r.Width / 2, r.Y + r.Height / 2);
        g.RotateTransform(-30);
        draw(new RectangleF(-r.Width / 2, -r.Height / 2, r.Width, r.Height));
        g.Restore(s);
    }

    private static void DrawTextMark(Graphics g, Size image, WatermarkOptions w)
    {
        using var family = new FontFamily("Segoe UI");
        const float probe = 100f;
        using var probePath = new GraphicsPath();
        probePath.AddString(w.Text, family, (int)FontStyle.Bold, probe, PointF.Empty, StringFormat.GenericTypographic);
        RectangleF nat = probePath.GetBounds();
        if (nat.Width <= 0 || nat.Height <= 0) return;

        bool tiled = w.Position == WatermarkPosition.Tiled;
        SizeF target = WatermarkOptions.TargetSize(image, nat.Size, tiled ? Math.Min(w.SizePercent, 40) : w.SizePercent);
        float k = target.Width / nat.Width;
        int alpha = (int)Math.Round(Math.Clamp(w.Opacity, 0, 100) * 2.55);
        using var fill = new SolidBrush(Color.FromArgb(alpha, w.Color));
        using var outline = new Pen(Color.FromArgb(alpha * 2 / 3, Luminance(w.Color) > 0.5 ? Color.Black : Color.White), Math.Max(1f, target.Height * 0.06f))
        {
            LineJoin = LineJoin.Round,
        };

        foreach (RectangleF r in WatermarkOptions.Place(image, target, w.Position))
        {
            Rotated(g, r, tiled, rr =>
            {
                using var path = (GraphicsPath)probePath.Clone();
                using var m = new Matrix();
                m.Translate(rr.X, rr.Y);
                m.Scale(k, k);
                m.Translate(-nat.X, -nat.Y);
                path.Transform(m);
                if (w.Outline) g.DrawPath(outline, path);
                g.FillPath(fill, path);
            });
        }
    }

    private static void DrawImageMark(Graphics g, Size image, WatermarkOptions w)
    {
        using var ms = new MemoryStream(w.ImagePng!);
        using var logo = Image.FromStream(ms);
        bool tiled = w.Position == WatermarkPosition.Tiled;
        SizeF target = WatermarkOptions.TargetSize(image, logo.Size, tiled ? Math.Min(w.SizePercent, 40) : w.SizePercent);
        using var attrs = new ImageAttributes();
        attrs.SetColorMatrix(new ColorMatrix { Matrix33 = Math.Clamp(w.Opacity, 0, 100) / 100f });
        foreach (RectangleF r in WatermarkOptions.Place(image, target, w.Position))
        {
            Rotated(g, r, tiled, rr =>
                g.DrawImage(logo, new[] { rr.Location, new PointF(rr.Right, rr.Top), new PointF(rr.Left, rr.Bottom) },
                    new RectangleF(0, 0, logo.Width, logo.Height), GraphicsUnit.Pixel, attrs));
        }
    }

    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

    /// <summary>
    /// Watermarks every image in <paramref name="folder"/> into <paramref name="outputFolder"/> (originals are never
    /// changed). Returns the number of files written; failures are listed in <paramref name="errors"/>.
    /// </summary>
    public static int ApplyToFolder(string folder, string outputFolder, WatermarkOptions w, List<string> errors, IProgress<int>? progress = null)
    {
        Directory.CreateDirectory(outputFolder);
        var files = Directory.EnumerateFiles(folder)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        int done = 0;
        foreach (string file in files)
        {
            try
            {
                Bitmap bmp;
                using (var fs = File.OpenRead(file))
                using (var img = Image.FromStream(fs))
                    bmp = ImageUtil.ToArgb(img);
                using (bmp)
                {
                    using (Graphics g = Graphics.FromImage(bmp)) Draw(g, bmp.Size, w);
                    string dest = Path.Combine(outputFolder, Path.GetFileName(file));
                    ImageUtil.SaveByExtension(bmp, dest, Color.White);
                }
                done++;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
            progress?.Report(done);
        }
        return done;
    }
}
