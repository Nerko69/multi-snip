using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MultiSnip;

public enum CombineLayout { Vertical, Horizontal, Grid }

public enum CombineAlignment { Start, Center, End }

public sealed class CombineOptions
{
    public CombineLayout Layout { get; set; } = CombineLayout.Vertical;
    public int GridColumns { get; set; } = 2;
    public CombineAlignment Alignment { get; set; } = CombineAlignment.Start;
    public int Spacing { get; set; } = 10;
    public int Margin { get; set; } = 0;
    public Color Background { get; set; } = Color.White;
    public bool TransparentBackground { get; set; }
    public bool Border { get; set; }
    public Color BorderColor { get; set; } = Color.FromArgb(200, 200, 200);
    public int BorderWidth { get; set; } = 1;

    /// <summary>
    /// Vertical: scale every snip to the widest width. Horizontal: scale to the tallest height.
    /// Grid: scale every snip to fit the largest cell.
    /// </summary>
    public bool MatchSize { get; set; }

    public CombineOptions Clone() => (CombineOptions)MemberwiseClone();
}

public static class ImageCombiner
{
    public static Bitmap Combine(IReadOnlyList<Image> images, CombineOptions o)
    {
        if (images.Count == 0)
            throw new ArgumentException("At least one image is required.", nameof(images));

        int bw = o.Border ? Math.Max(0, o.BorderWidth) : 0;
        int spacing = Math.Max(0, o.Spacing);
        int margin = Math.Max(0, o.Margin);
        Size[] sizes = ComputeImageSizes(images, o);

        // Each "box" is the image plus its border.
        Size[] boxes = sizes.Select(s => new Size(s.Width + 2 * bw, s.Height + 2 * bw)).ToArray();
        var placements = new Rectangle[images.Count];
        int totalW, totalH;

        switch (o.Layout)
        {
            case CombineLayout.Horizontal:
            {
                int rowH = boxes.Max(b => b.Height);
                int x = margin;
                for (int i = 0; i < boxes.Length; i++)
                {
                    int y = margin + AlignOffset(rowH, boxes[i].Height, o.Alignment);
                    placements[i] = new Rectangle(new Point(x, y), boxes[i]);
                    x += boxes[i].Width + spacing;
                }
                totalW = x - spacing + margin;
                totalH = rowH + 2 * margin;
                break;
            }
            case CombineLayout.Grid:
            {
                int cols = Math.Clamp(o.GridColumns, 1, boxes.Length);
                int rows = (boxes.Length + cols - 1) / cols;
                var colW = new int[cols];
                var rowH = new int[rows];
                for (int i = 0; i < boxes.Length; i++)
                {
                    colW[i % cols] = Math.Max(colW[i % cols], boxes[i].Width);
                    rowH[i / cols] = Math.Max(rowH[i / cols], boxes[i].Height);
                }
                var colX = new int[cols];
                var rowY = new int[rows];
                for (int c = 0, x = margin; c < cols; x += colW[c] + spacing, c++) colX[c] = x;
                for (int r = 0, y = margin; r < rows; y += rowH[r] + spacing, r++) rowY[r] = y;
                for (int i = 0; i < boxes.Length; i++)
                {
                    int c = i % cols, r = i / cols;
                    placements[i] = new Rectangle(
                        colX[c] + AlignOffset(colW[c], boxes[i].Width, o.Alignment),
                        rowY[r] + AlignOffset(rowH[r], boxes[i].Height, o.Alignment),
                        boxes[i].Width, boxes[i].Height);
                }
                totalW = colW.Sum() + spacing * (cols - 1) + 2 * margin;
                totalH = rowH.Sum() + spacing * (rows - 1) + 2 * margin;
                break;
            }
            default: // Vertical
            {
                int colW = boxes.Max(b => b.Width);
                int y = margin;
                for (int i = 0; i < boxes.Length; i++)
                {
                    int x = margin + AlignOffset(colW, boxes[i].Width, o.Alignment);
                    placements[i] = new Rectangle(new Point(x, y), boxes[i]);
                    y += boxes[i].Height + spacing;
                }
                totalW = colW + 2 * margin;
                totalH = y - spacing + margin;
                break;
            }
        }

        var result = new Bitmap(Math.Max(1, totalW), Math.Max(1, totalH), PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.Clear(o.TransparentBackground ? Color.Transparent : Color.FromArgb(255, o.Background));

        using var borderBrush = new SolidBrush(o.BorderColor);
        for (int i = 0; i < images.Count; i++)
        {
            Rectangle box = placements[i];
            if (bw > 0)
            {
                // Frame only, so a transparent background stays transparent behind the image.
                g.FillRectangle(borderBrush, box.X, box.Y, box.Width, bw);
                g.FillRectangle(borderBrush, box.X, box.Bottom - bw, box.Width, bw);
                g.FillRectangle(borderBrush, box.X, box.Y + bw, bw, box.Height - 2 * bw);
                g.FillRectangle(borderBrush, box.Right - bw, box.Y + bw, bw, box.Height - 2 * bw);
            }

            var dest = new Rectangle(box.X + bw, box.Y + bw, sizes[i].Width, sizes[i].Height);
            bool scaled = dest.Size != images[i].Size;
            g.InterpolationMode = scaled ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = scaled ? PixelOffsetMode.HighQuality : PixelOffsetMode.Half;
            g.SmoothingMode = SmoothingMode.None;
            using var attrs = new ImageAttributes();
            attrs.SetWrapMode(WrapMode.TileFlipXY); // avoids faint edge artifacts when scaling
            g.DrawImage(images[i], dest, 0, 0, images[i].Width, images[i].Height, GraphicsUnit.Pixel, attrs);
        }
        return result;
    }

    private static Size[] ComputeImageSizes(IReadOnlyList<Image> images, CombineOptions o)
    {
        Size[] sizes = images.Select(i => i.Size).ToArray();
        if (!o.MatchSize || images.Count < 2)
            return sizes;

        switch (o.Layout)
        {
            case CombineLayout.Vertical:
            {
                int w = sizes.Max(s => s.Width);
                return sizes.Select(s => new Size(w, Math.Max(1, (int)Math.Round(s.Height * (double)w / s.Width)))).ToArray();
            }
            case CombineLayout.Horizontal:
            {
                int h = sizes.Max(s => s.Height);
                return sizes.Select(s => new Size(Math.Max(1, (int)Math.Round(s.Width * (double)h / s.Height)), h)).ToArray();
            }
            default:
            {
                int cw = sizes.Max(s => s.Width), ch = sizes.Max(s => s.Height);
                return sizes.Select(s =>
                {
                    double k = Math.Min(cw / (double)s.Width, ch / (double)s.Height);
                    return new Size(Math.Max(1, (int)Math.Round(s.Width * k)), Math.Max(1, (int)Math.Round(s.Height * k)));
                }).ToArray();
            }
        }
    }

    private static int AlignOffset(int available, int size, CombineAlignment a) => a switch
    {
        CombineAlignment.Center => (available - size) / 2,
        CombineAlignment.End => available - size,
        _ => 0,
    };
}
