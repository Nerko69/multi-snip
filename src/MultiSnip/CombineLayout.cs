namespace MultiSnip;

/// <summary>
/// How snips are put together. Overlay: the first snip is the background and the others are placed on top of it,
/// each with its own position and size.
/// </summary>
public enum CombineLayout { Vertical, Horizontal, Grid, Overlay }

public enum CombineAlignment { Start, Center, End }

public sealed class CombineOptions
{
    public CombineLayout Layout { get; set; } = CombineLayout.Vertical;

    /// <summary>Grid columns; 0 = automatic (as square as possible).</summary>
    public int GridColumns { get; set; }
    public CombineAlignment Alignment { get; set; } = CombineAlignment.Start;
    public int Spacing { get; set; } = 10;
    public int Margin { get; set; } = 0;
    public Color Background { get; set; } = Color.White;
    public bool TransparentBackground { get; set; }
    public bool Border { get; set; }
    public Color BorderColor { get; set; } = Color.FromArgb(200, 200, 200);
    public int BorderWidth { get; set; } = 1;

    /// <summary>Draw a line in the gap between snips (not in Overlay mode).</summary>
    public bool Divider { get; set; }
    public Color DividerColor { get; set; } = Color.FromArgb(0, 120, 215);
    public int DividerWidth { get; set; } = 2;

    /// <summary>
    /// Vertical: scale every snip to the widest width. Horizontal: scale to the tallest height.
    /// Grid: scale every snip to fit the largest cell.
    /// </summary>
    public bool MatchSize { get; set; }

    /// <summary>Size of the saved/copied image in percent (100 = original pixels).</summary>
    public int OutputScale { get; set; } = 100;

    public WatermarkOptions Watermark { get; set; } = new();

    public CombineOptions Clone()
    {
        var c = (CombineOptions)MemberwiseClone();
        c.Watermark = Watermark.Clone();
        return c;
    }

    /// <summary>Actual number of grid columns for <paramref name="count"/> snips.</summary>
    public int EffectiveColumns(int count)
    {
        if (count <= 0) return 1;
        int cols = GridColumns > 0 ? GridColumns : (int)Math.Ceiling(Math.Sqrt(count));
        return Math.Clamp(cols, 1, count);
    }

    /// <summary>Gap between snips: at least as wide as the divider line, so the line never covers a snip.</summary>
    public int EffectiveSpacing => Math.Max(Math.Max(0, Spacing), Divider ? Math.Max(1, DividerWidth) : 0);
}

/// <summary>Where every snip goes in the combined image (pure geometry, no drawing).</summary>
public sealed class CombinedLayout
{
    public required Size Total { get; init; }

    /// <summary>Image rectangle of each snip (inside its border).</summary>
    public required Rectangle[] ImageRects { get; init; }

    /// <summary>Image plus border of each snip.</summary>
    public required Rectangle[] Boxes { get; init; }

    /// <summary>Divider lines between snips (empty when dividers are off).</summary>
    public Rectangle[] Dividers { get; init; } = Array.Empty<Rectangle>();

    /// <summary>A single image shown on its own (used to view one snip).</summary>
    public static CombinedLayout Single(Size size) => new()
    {
        Total = size,
        ImageRects = new[] { new Rectangle(Point.Empty, size) },
        Boxes = new[] { new Rectangle(Point.Empty, size) },
    };

    /// <summary>Default place for an overlay snip: centred on the background, at most 60% of its size, cascaded.</summary>
    public static RectangleF DefaultOverlayRect(Size background, Size snip, int index)
    {
        float k = Math.Min(1f, Math.Min(0.6f * background.Width / snip.Width, 0.6f * background.Height / snip.Height));
        float w = Math.Max(1, snip.Width * k), h = Math.Max(1, snip.Height * k);
        float step = 24 * Math.Max(0, index - 1);
        float x = Math.Clamp((background.Width - w) / 2 + step, 0, Math.Max(0, background.Width - w));
        float y = Math.Clamp((background.Height - h) / 2 + step, 0, Math.Max(0, background.Height - h));
        return new RectangleF(x, y, w, h);
    }

    /// <param name="imageSizes">Size of each snip.</param>
    /// <param name="o">Options.</param>
    /// <param name="overlayRects">Overlay mode: position/size of each snip after the first, relative to the
    /// background snip's top-left (null entries get a default place).</param>
    public static CombinedLayout Compute(IReadOnlyList<Size> imageSizes, CombineOptions o, IReadOnlyList<RectangleF?>? overlayRects = null)
    {
        if (imageSizes.Count == 0)
            return new CombinedLayout { Total = new Size(1, 1), ImageRects = Array.Empty<Rectangle>(), Boxes = Array.Empty<Rectangle>() };

        int bw = o.Border ? Math.Max(0, o.BorderWidth) : 0;
        int margin = Math.Max(0, o.Margin);
        if (o.Layout == CombineLayout.Overlay)
            return ComputeOverlay(imageSizes, overlayRects, bw, margin);

        int spacing = o.EffectiveSpacing;
        Size[] sizes = ScaledSizes(imageSizes, o);

        // Each "box" is the image plus its border.
        Size[] boxes = sizes.Select(s => new Size(s.Width + 2 * bw, s.Height + 2 * bw)).ToArray();
        var placements = new Rectangle[boxes.Length];
        var dividers = new List<Rectangle>();
        int dw = o.Divider ? Math.Max(1, o.DividerWidth) : 0;
        int totalW, totalH;

        switch (o.Layout)
        {
            case CombineLayout.Horizontal:
            {
                int rowH = boxes.Max(b => b.Height);
                int x = margin;
                for (int i = 0; i < boxes.Length; i++)
                {
                    if (i > 0 && dw > 0) dividers.Add(new Rectangle(x - spacing + (spacing - dw) / 2, margin, dw, rowH));
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
                int cols = o.EffectiveColumns(boxes.Length);
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
                if (dw > 0)
                {
                    for (int c = 1; c < cols; c++) dividers.Add(new Rectangle(colX[c] - spacing + (spacing - dw) / 2, margin, dw, totalH - 2 * margin));
                    for (int r = 1; r < rows; r++) dividers.Add(new Rectangle(margin, rowY[r] - spacing + (spacing - dw) / 2, totalW - 2 * margin, dw));
                }
                break;
            }
            default: // Vertical
            {
                int colW = boxes.Max(b => b.Width);
                int y = margin;
                for (int i = 0; i < boxes.Length; i++)
                {
                    if (i > 0 && dw > 0) dividers.Add(new Rectangle(margin, y - spacing + (spacing - dw) / 2, colW, dw));
                    int x = margin + AlignOffset(colW, boxes[i].Width, o.Alignment);
                    placements[i] = new Rectangle(new Point(x, y), boxes[i]);
                    y += boxes[i].Height + spacing;
                }
                totalW = colW + 2 * margin;
                totalH = y - spacing + margin;
                break;
            }
        }

        var imageRects = placements.Select((b, i) => new Rectangle(b.X + bw, b.Y + bw, sizes[i].Width, sizes[i].Height)).ToArray();
        return new CombinedLayout
        {
            Total = new Size(Math.Max(1, totalW), Math.Max(1, totalH)),
            ImageRects = imageRects,
            Boxes = placements,
            Dividers = dividers.ToArray(),
        };
    }

    private static CombinedLayout ComputeOverlay(IReadOnlyList<Size> sizes, IReadOnlyList<RectangleF?>? overlayRects, int bw, int margin)
    {
        Size bg = sizes[0];
        var imageRects = new Rectangle[sizes.Count];
        int ox = margin + bw, oy = margin + bw; // background image origin
        imageRects[0] = new Rectangle(ox, oy, bg.Width, bg.Height);
        for (int i = 1; i < sizes.Count; i++)
        {
            RectangleF r = (overlayRects != null && i < overlayRects.Count ? overlayRects[i] : null) ?? DefaultOverlayRect(bg, sizes[i], i);
            imageRects[i] = new Rectangle(ox + (int)Math.Round(r.X), oy + (int)Math.Round(r.Y),
                Math.Max(1, (int)Math.Round(r.Width)), Math.Max(1, (int)Math.Round(r.Height)));
        }
        var boxes = imageRects.Select(r => Rectangle.Inflate(r, bw, bw)).ToArray();
        return new CombinedLayout
        {
            Total = new Size(bg.Width + 2 * (bw + margin), bg.Height + 2 * (bw + margin)),
            ImageRects = imageRects,
            Boxes = boxes,
        };
    }

    private static Size[] ScaledSizes(IReadOnlyList<Size> input, CombineOptions o)
    {
        Size[] sizes = input.ToArray();
        if (!o.MatchSize || sizes.Length < 2)
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
