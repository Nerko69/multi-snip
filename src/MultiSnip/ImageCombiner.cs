using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MultiSnip;

public static class ImageCombiner
{
    /// <summary>Draws the snips (without annotations) at the positions given by <paramref name="layout"/>.</summary>
    public static Bitmap Combine(IReadOnlyList<Image> images, CombineOptions o, CombinedLayout layout)
    {
        var result = new Bitmap(layout.Total.Width, layout.Total.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.Clear(o.TransparentBackground ? Color.Transparent : Color.FromArgb(255, o.Background));

        int bw = o.Border ? Math.Max(0, o.BorderWidth) : 0;
        using var borderBrush = new SolidBrush(o.BorderColor);
        for (int i = 0; i < images.Count; i++)
        {
            Rectangle box = layout.Boxes[i];
            if (bw > 0)
            {
                // Frame only, so a transparent background stays transparent behind the image.
                g.FillRectangle(borderBrush, box.X, box.Y, box.Width, bw);
                g.FillRectangle(borderBrush, box.X, box.Bottom - bw, box.Width, bw);
                g.FillRectangle(borderBrush, box.X, box.Y + bw, bw, box.Height - 2 * bw);
                g.FillRectangle(borderBrush, box.Right - bw, box.Y + bw, bw, box.Height - 2 * bw);
            }

            Rectangle dest = layout.ImageRects[i];
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
}
