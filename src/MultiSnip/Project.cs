using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MultiSnip;

/// <summary>
/// One snip: the original image (never modified in place) plus the drawings on top of it.
/// A "Keep as one" group is also a snip: its image is the combined picture of its children, which are kept so
/// that Ungroup can bring them back.
/// </summary>
public sealed class SnipItem
{
    public SnipItem(Bitmap image, string source, List<Annotation>? annotations = null,
        List<SnipItem>? children = null, CombineOptions? groupOptions = null, RectangleF? overlayRect = null)
    {
        Image = image;
        Source = source;
        Annotations = annotations ?? new List<Annotation>();
        Children = children;
        GroupOptions = groupOptions;
        OverlayRect = overlayRect;
    }

    /// <summary>The picture. Shared between undo snapshots, so it is never disposed or edited.</summary>
    public Bitmap Image { get; }
    public string Source { get; }
    public List<Annotation> Annotations { get; private set; }

    /// <summary>Group: the snips it was made from (unchanged while grouped).</summary>
    public List<SnipItem>? Children { get; }
    public CombineOptions? GroupOptions { get; }
    public bool IsGroup => Children != null;

    /// <summary>Overlay mode: position and size on the background snip (null = default place).</summary>
    public RectangleF? OverlayRect { get; set; }

    /// <summary>Cached list thumbnail; cleared whenever the drawings change.</summary>
    public Bitmap? Thumbnail { get; set; }

    public SnipItem Snapshot() =>
        new(Image, Source, Annotations.Select(a => a.Clone()).ToList(), Children, GroupOptions?.Clone(), OverlayRect);

    /// <summary>Same drawings and overlay place, new picture (used by crop / erase).</summary>
    public SnipItem WithImage(Bitmap image, List<Annotation> annotations, RectangleF? overlayRect) =>
        new(image, Source, annotations, null, null, overlayRect);

    /// <summary>Not disposed here: the list's ImageList may still reference it until it is rebuilt.</summary>
    public void InvalidateThumbnail() => Thumbnail = null;
}

/// <summary>
/// The work in one tab: its snips (each with its drawings), combine options, save location and undo history.
/// </summary>
public sealed class Project
{
    private const int MaxUndo = 60;

    private sealed record State(List<SnipItem> Snips, CombineOptions Options, int NextBadge);

    private readonly Stack<State> _undo = new();
    private readonly Stack<State> _redo = new();
    private static int _untitledCounter;

    public Project(CombineOptions options)
    {
        Options = options;
        UntitledName = $"Untitled {++_untitledCounter}";
    }

    public List<SnipItem> Snips { get; private set; } = new();
    public CombineOptions Options { get; private set; }

    /// <summary>Where this tab was last saved/opened (each tab keeps its own location).</summary>
    public string? FilePath { get; set; }
    public bool Dirty { get; set; }
    public int NextBadgeNumber { get; set; } = 1;

    /// <summary>Zoom factor (1 = 100%); null = fit to window.</summary>
    public float? Zoom { get; set; }

    /// <summary>Snip shown on its own in the main window, or -1 for the combined image.</summary>
    public int ViewIndex { get; set; } = -1;

    public string UntitledName { get; }
    public string DisplayName => FilePath != null ? Path.GetFileNameWithoutExtension(FilePath) : UntitledName;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Call before every change: remembers the current state for Undo and marks the tab as modified.</summary>
    public void Checkpoint()
    {
        _undo.Push(Capture());
        _redo.Clear();
        TrimUndo();
        Dirty = true;
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        _redo.Push(Capture());
        Restore(_undo.Pop());
        Dirty = true;
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        _undo.Push(Capture());
        Restore(_redo.Pop());
        Dirty = true;
        return true;
    }

    private State Capture() => new(Snips.Select(s => s.Snapshot()).ToList(), Options.Clone(), NextBadgeNumber);

    private void Restore(State s)
    {
        Snips = s.Snips.Select(x => x.Snapshot()).ToList();
        Options = s.Options.Clone();
        NextBadgeNumber = s.NextBadge;
        if (ViewIndex >= Snips.Count) ViewIndex = -1;
    }

    private void TrimUndo()
    {
        if (_undo.Count <= MaxUndo) return;
        var keep = _undo.Take(MaxUndo).Reverse().ToList();
        _undo.Clear();
        foreach (State s in keep) _undo.Push(s);
    }

    /// <summary>Highest badge number used so far, plus one.</summary>
    public int SuggestNextBadge() =>
        Snips.SelectMany(s => s.Annotations).Select(a => a.BadgeNumber ?? 0).DefaultIfEmpty(0).Max() + 1;

    // ------------------------------------------------------------------ rendering

    public static CombinedLayout ComputeLayout(IReadOnlyList<SnipItem> snips, CombineOptions o) =>
        CombinedLayout.Compute(snips.Select(s => s.Image.Size).ToList(), o, snips.Select(s => s.OverlayRect).ToList());

    public CombinedLayout ComputeLayout() => ComputeLayout(Snips, Options);

    /// <summary>The snips combined, without drawings (the canvas draws those live on top).</summary>
    public Bitmap RenderBase(CombinedLayout layout) => ImageCombiner.Combine(Snips.Select(s => (Image)s.Image).ToList(), Options, layout);

    /// <summary>Snips + their drawings combined with the given options (no watermark, full size).</summary>
    public static Bitmap RenderComposite(IReadOnlyList<SnipItem> snips, CombineOptions o)
    {
        CombinedLayout layout = ComputeLayout(snips, o);
        Bitmap full = ImageCombiner.Combine(snips.Select(s => (Image)s.Image).ToList(), o, layout);
        using Graphics g = Graphics.FromImage(full);
        DrawAnnotations(g, layout, snips);
        return full;
    }

    /// <summary>The finished image: snips + drawings + watermark, at the chosen output size.</summary>
    public Bitmap RenderFinal()
    {
        Bitmap full = RenderComposite(Snips, Options);
        using (Graphics g = Graphics.FromImage(full))
            WatermarkRenderer.Draw(g, full.Size, Options.Watermark);
        return Scale(full, Options.OutputScale);
    }

    /// <summary>One snip with its drawings (and the tab's watermark), for "save each snip" / "copy this snip".</summary>
    public Bitmap RenderSnip(SnipItem s, bool watermark = true)
    {
        var bmp = new Bitmap(s.Image.Width, s.Image.Height, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(bmp);
        g.DrawImage(s.Image, 0, 0, s.Image.Width, s.Image.Height);
        AnnotationRenderer.DrawAll(g, s.Annotations);
        if (watermark) WatermarkRenderer.Draw(g, bmp.Size, Options.Watermark);
        return bmp;
    }

    private static Bitmap Scale(Bitmap full, int percent)
    {
        int scale = Math.Clamp(percent, 5, 100);
        if (scale == 100) return full;
        var scaled = new Bitmap(Math.Max(1, full.Width * scale / 100), Math.Max(1, full.Height * scale / 100), PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(full, new Rectangle(Point.Empty, scaled.Size));
        }
        full.Dispose();
        return scaled;
    }

    /// <summary>Draws every snip's annotations at its place in the layout (g is in combined-image pixels).</summary>
    public static void DrawAnnotations(Graphics g, CombinedLayout layout, IReadOnlyList<SnipItem> snips)
    {
        for (int i = 0; i < snips.Count && i < layout.ImageRects.Length; i++)
        {
            if (snips[i].Annotations.Count == 0) continue;
            GraphicsState state = g.Save();
            ApplySnipTransform(g, layout, i, snips[i].Image.Size);
            AnnotationRenderer.DrawAll(g, snips[i].Annotations);
            g.Restore(state);
        }
    }

    /// <summary>Maps snip pixels to combined-image pixels (snips can be scaled by "Match sizes" / overlay size).</summary>
    public static void ApplySnipTransform(Graphics g, CombinedLayout layout, int index, Size image)
    {
        Rectangle r = layout.ImageRects[index];
        g.TranslateTransform(r.X, r.Y);
        g.ScaleTransform(r.Width / (float)image.Width, r.Height / (float)image.Height);
    }

    // ------------------------------------------------------------------ editing snips

    /// <summary>"Keep as one": turns the given snips (at least 2) into one group snip, placed where the first was.</summary>
    public bool Group(IReadOnlyCollection<int> indices)
    {
        var sorted = indices.Where(i => i >= 0 && i < Snips.Count).Distinct().OrderBy(i => i).ToList();
        if (sorted.Count < 2) return false;
        var children = sorted.Select(i => Snips[i].Snapshot()).ToList();
        CombineOptions o = Options.Clone();
        o.Margin = 0;                // the outer margin belongs to the final image
        o.OutputScale = 100;
        o.Watermark = new WatermarkOptions();
        Bitmap image = RenderComposite(children, o);
        Checkpoint();
        foreach (int i in Enumerable.Reverse(sorted)) Snips.RemoveAt(i);
        Snips.Insert(sorted[0], new SnipItem(image, "Group", null, children, o));
        ViewIndex = -1;
        return true;
    }

    /// <summary>Puts a group's snips back in its place. Drawings made on the group itself are dropped.</summary>
    public bool Ungroup(int index)
    {
        if (index < 0 || index >= Snips.Count || Snips[index].Children is not { } children) return false;
        Checkpoint();
        Snips.RemoveAt(index);
        Snips.InsertRange(index, children.Select(c => c.Snapshot()));
        ViewIndex = -1;
        return true;
    }

    /// <summary>Crops a snip to <paramref name="area"/> (snip pixels). Drawings move with the picture.</summary>
    public bool Crop(int index, Rectangle area)
    {
        SnipItem s = Snips[index];
        area.Intersect(new Rectangle(Point.Empty, s.Image.Size));
        if (area.Width < 2 || area.Height < 2) return false;
        Checkpoint();
        Bitmap cropped = s.Image.Clone(area, PixelFormat.Format32bppArgb);
        var annotations = s.Annotations.Select(a => { Annotation c = a.Clone(); c.Offset(-area.X, -area.Y); return c; }).ToList();
        RectangleF? overlay = null;
        if (s.OverlayRect is { } r)
        {
            float kx = r.Width / s.Image.Width, ky = r.Height / s.Image.Height;
            overlay = new RectangleF(r.X + area.X * kx, r.Y + area.Y * ky, area.Width * kx, area.Height * ky);
        }
        Snips[index] = s.WithImage(cropped, annotations, overlay);
        return true;
    }

    /// <summary>Erases an area of a snip's picture (like Paint's selection + Delete), filling it with <paramref name="fill"/>.</summary>
    public bool EraseArea(int index, Rectangle area, Color fill)
    {
        SnipItem s = Snips[index];
        area.Intersect(new Rectangle(Point.Empty, s.Image.Size));
        if (area.Width < 1 || area.Height < 1) return false;
        Checkpoint();
        Bitmap copy = s.Image.Clone(new Rectangle(Point.Empty, s.Image.Size), PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(copy))
        {
            g.CompositingMode = CompositingMode.SourceCopy; // so "transparent" really clears the pixels
            using var b = new SolidBrush(fill);
            g.FillRectangle(b, area);
        }
        Snips[index] = s.WithImage(copy, s.Annotations.Select(a => a.Clone()).ToList(), s.OverlayRect);
        return true;
    }
}
