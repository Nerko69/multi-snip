namespace MultiSnip;

public enum WatermarkKind { Text, Image }

public enum WatermarkPosition { TopLeft, TopCenter, TopRight, MiddleLeft, Center, MiddleRight, BottomLeft, BottomCenter, BottomRight, Tiled }

/// <summary>A text or image watermark stamped on the finished image (and on bulk-watermarked files).</summary>
public sealed class WatermarkOptions
{
    public bool Enabled { get; set; }
    public WatermarkKind Kind { get; set; } = WatermarkKind.Text;
    public string Text { get; set; } = "© WebProgress.AI";
    public Color Color { get; set; } = Color.White;

    /// <summary>0 = invisible, 100 = solid.</summary>
    public int Opacity { get; set; } = 45;

    /// <summary>Watermark width as a percentage of the image width.</summary>
    public int SizePercent { get; set; } = 25;
    public WatermarkPosition Position { get; set; } = WatermarkPosition.BottomRight;

    /// <summary>Thin dark outline behind text so it stays readable on light and dark images.</summary>
    public bool Outline { get; set; } = true;

    /// <summary>Image watermark (e.g. a logo) as PNG bytes.</summary>
    public byte[]? ImagePng { get; set; }

    public WatermarkOptions Clone()
    {
        var c = (WatermarkOptions)MemberwiseClone();
        c.ImagePng = ImagePng?.ToArray();
        return c;
    }

    public bool IsUsable => Enabled && (Kind == WatermarkKind.Text ? !string.IsNullOrWhiteSpace(Text) : ImagePng is { Length: > 0 });

    /// <summary>Size of the watermark on an image: <see cref="SizePercent"/> of the image width, keeping proportions.</summary>
    public static SizeF TargetSize(Size image, SizeF natural, int sizePercent)
    {
        if (natural.Width <= 0 || natural.Height <= 0) return SizeF.Empty;
        float w = Math.Max(8f, image.Width * Math.Clamp(sizePercent, 1, 100) / 100f);
        float k = w / natural.Width;
        // Never taller than the image.
        if (natural.Height * k > image.Height) k = image.Height / natural.Height;
        return new SizeF(natural.Width * k, natural.Height * k);
    }

    /// <summary>Where to draw the watermark (several places when tiled), in image pixels.</summary>
    public static List<RectangleF> Place(Size image, SizeF mark, WatermarkPosition pos)
    {
        var result = new List<RectangleF>();
        if (mark.Width <= 0 || mark.Height <= 0) return result;
        float margin = Math.Max(4f, Math.Min(image.Width, image.Height) * 0.02f);
        if (pos == WatermarkPosition.Tiled)
        {
            float stepX = mark.Width * 1.6f, stepY = mark.Height * 3f;
            int row = 0;
            for (float y = -mark.Height; y < image.Height + mark.Height; y += stepY, row++)
            {
                float offset = row % 2 == 0 ? 0 : stepX / 2;
                for (float x = -mark.Width + offset; x < image.Width + mark.Width; x += stepX)
                    result.Add(new RectangleF(x, y, mark.Width, mark.Height));
            }
            return result;
        }

        int col = (int)pos % 3, rowIndex = (int)pos / 3;
        float px = col switch { 0 => margin, 1 => (image.Width - mark.Width) / 2, _ => image.Width - mark.Width - margin };
        float py = rowIndex switch { 0 => margin, 1 => (image.Height - mark.Height) / 2, _ => image.Height - mark.Height - margin };
        result.Add(new RectangleF(px, py, mark.Width, mark.Height));
        return result;
    }
}
