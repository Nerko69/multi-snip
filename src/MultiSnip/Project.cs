using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MultiSnip;

/// <summary>One snip: the original image (never modified) plus the drawings on top of it.</summary>
public sealed class SnipItem
{
    public SnipItem(Bitmap image, string source, List<Annotation>? annotations = null)
    {
        Image = image;
        Source = source;
        Annotations = annotations ?? new List<Annotation>();
    }

    /// <summary>The original capture. Shared between undo snapshots, so it is never disposed or edited.</summary>
    public Bitmap Image { get; }
    public string Source { get; }
    public List<Annotation> Annotations { get; private set; }

    /// <summary>Cached list thumbnail; cleared whenever the annotations change.</summary>
    public Bitmap? Thumbnail { get; set; }

    public SnipItem Snapshot() => new(Image, Source, Annotations.Select(a => a.Clone()).ToList());

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

    public CombinedLayout ComputeLayout() => CombinedLayout.Compute(Snips.Select(s => s.Image.Size).ToList(), Options);

    /// <summary>The snips combined, without drawings (the canvas draws those live on top).</summary>
    public Bitmap RenderBase(CombinedLayout layout) => ImageCombiner.Combine(Snips.Select(s => (Image)s.Image).ToList(), Options, layout);

    /// <summary>The finished image: snips + drawings, at the chosen output size.</summary>
    public Bitmap RenderFinal()
    {
        CombinedLayout layout = ComputeLayout();
        Bitmap full = RenderBase(layout);
        using (Graphics g = Graphics.FromImage(full))
            DrawAnnotations(g, layout);

        int scale = Math.Clamp(Options.OutputScale, 5, 100);
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
    public void DrawAnnotations(Graphics g, CombinedLayout layout)
    {
        for (int i = 0; i < Snips.Count && i < layout.ImageRects.Length; i++)
        {
            if (Snips[i].Annotations.Count == 0) continue;
            GraphicsState state = g.Save();
            ApplySnipTransform(g, i, layout);
            AnnotationRenderer.DrawAll(g, Snips[i].Annotations);
            g.Restore(state);
        }
    }

    /// <summary>Maps snip pixels to combined-image pixels (snips can be scaled by "Match sizes").</summary>
    public void ApplySnipTransform(Graphics g, int index, CombinedLayout layout)
    {
        Rectangle r = layout.ImageRects[index];
        Size img = Snips[index].Image.Size;
        g.TranslateTransform(r.X, r.Y);
        g.ScaleTransform(r.Width / (float)img.Width, r.Height / (float)img.Height);
    }
}
