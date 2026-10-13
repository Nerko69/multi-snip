namespace MultiSnip;

/// <summary>
/// Stitches successive captures of the same screen area, taken while the content scrolls down,
/// into one tall image. Works on raw 32-bit pixel rows (no GDI+), so it can be unit-tested anywhere.
/// <para>
/// Fixed areas that look identical in every frame (sticky headers / footers) are detected and kept
/// only once. The scroll distance between two frames is found by matching row fingerprints.
/// </para>
/// </summary>
public sealed class ScrollStitcher
{
    public enum StepResult { First, NoChange, Appended, NoMatch, LimitReached }

    private sealed class Frame
    {
        public required int[] Pixels;
        public required ulong[] RowHash;
        public required bool[] Uniform;
    }

    private readonly int _width;
    private readonly int _height;
    private readonly int _hashWidth;
    private readonly int _maxHeight;
    private readonly List<int[]> _rows = new(); // stitched output (excluding the footer)
    private Frame? _first;
    private Frame? _prev;
    private int _top = -1;   // fixed header height (rows identical in every frame)
    private int _bottom;     // fixed footer height
    private int? _lastShift;

    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    /// <param name="maxHeight">Stop growing once the stitched image reaches this height.</param>
    public ScrollStitcher(int width, int height, int maxHeight = 30000)
    {
        _width = width;
        _height = height;
        _maxHeight = maxHeight;
        // Ignore a strip on the right when comparing rows: the scrollbar thumb moves on every step.
        _hashWidth = Math.Max(1, width - Math.Min(32, width / 8));
    }

    public int Frames { get; private set; }
    public int StitchedHeight => _rows.Count + (_top < 0 ? _height : _bottom);

    /// <summary>Adds the next capture (row-major ARGB, <c>width*height</c> values).</summary>
    public StepResult Add(int[] pixels)
    {
        if (pixels.Length != _width * _height)
            throw new ArgumentException("Frame size does not match.", nameof(pixels));

        Frame f = MakeFrame(pixels);
        if (_prev == null)
        {
            _first = _prev = f;
            Frames = 1;
            return StepResult.First;
        }

        if (f.RowHash.AsSpan().SequenceEqual(_prev.RowHash))
            return StepResult.NoChange; // reached the end (or the area did not scroll)

        if (StitchedHeight >= _maxHeight)
            return StepResult.LimitReached;

        if (_top < 0)
        {
            // Second frame: learn the fixed header/footer, then emit the first frame (minus footer).
            (_top, _bottom) = DetectStatic(_first!, f);
            for (int r = 0; r < _height - _bottom; r++) _rows.Add(Row(_first!, r));
        }

        int band = _height - _top - _bottom;
        int? shift = FindShift(_prev, f, band);
        StepResult result = StepResult.Appended;
        if (shift == null)
        {
            // Could not find the overlap (e.g. it scrolled further than one screen): keep everything.
            shift = band;
            result = StepResult.NoMatch;
        }
        else
        {
            _lastShift = shift;
        }

        // New content is the bottom `shift` rows of the scrolling band.
        for (int r = _height - _bottom - shift.Value; r < _height - _bottom; r++) _rows.Add(Row(f, r));
        _prev = f;
        Frames++;
        return result;
    }

    /// <summary>Returns the stitched image as row-major ARGB pixels.</summary>
    public int[] Build(out int height)
    {
        if (_prev == null) throw new InvalidOperationException("No frames were added.");
        if (_top < 0)
        {
            height = _height;
            return (int[])_first!.Pixels.Clone();
        }

        height = _rows.Count + _bottom;
        var result = new int[_width * height];
        int y = 0;
        foreach (int[] row in _rows) row.CopyTo(result, _width * y++);
        Array.Copy(_prev.Pixels, _width * (_height - _bottom), result, _width * y, _width * _bottom); // footer from last frame
        return result;
    }

    private int[] Row(Frame f, int r) => f.Pixels.AsSpan(r * _width, _width).ToArray();

    private Frame MakeFrame(int[] px)
    {
        var hash = new ulong[_height];
        var uniform = new bool[_height];
        for (int r = 0; r < _height; r++)
        {
            ulong h = 14695981039346656037UL; // FNV-1a
            int start = r * _width;
            int first = px[start];
            bool same = true;
            for (int x = 0; x < _hashWidth; x++)
            {
                int p = px[start + x];
                same &= p == first;
                h = (h ^ (uint)p) * 1099511628211UL;
            }
            hash[r] = h;
            uniform[r] = same;
        }
        return new Frame { Pixels = px, RowHash = hash, Uniform = uniform };
    }

    private (int top, int bottom) DetectStatic(Frame a, Frame b)
    {
        int cap = _height * 2 / 5;
        int top = 0;
        while (top < cap && a.RowHash[top] == b.RowHash[top]) top++;
        int bottom = 0;
        while (bottom < cap && a.RowHash[_height - 1 - bottom] == b.RowHash[_height - 1 - bottom]) bottom++;
        return (top, bottom);
    }

    /// <summary>
    /// Finds how many rows the content moved up between <paramref name="a"/> and <paramref name="b"/>:
    /// b[top + j] == a[top + j + shift]. Returns null when no convincing match exists.
    /// </summary>
    private int? FindShift(Frame a, Frame b, int band)
    {
        int minOverlap = Math.Max(8, band / 10);
        int? best = null;
        int bestScore = int.MinValue;

        for (int dy = 1; dy <= band - minOverlap; dy++)
        {
            int overlap = band - dy;
            // Skip a margin at both ends of the overlap: sticky headers/footers that change while
            // scrolling (shadows, "back to top" buttons...) would otherwise spoil the match.
            int skip = Math.Min(band / 8, overlap / 4);
            int compared = overlap - 2 * skip;
            int allowed = compared / 33; // ~3% tolerance for carets, animations, hover effects
            int mismatches = 0, informative = 0;
            for (int j = skip; j < overlap - skip; j++)
            {
                int ra = _top + dy + j, rb = _top + j;
                if (a.RowHash[ra] != b.RowHash[rb])
                {
                    if (++mismatches > allowed) break;
                }
                else if (!a.Uniform[ra])
                {
                    informative++;
                }
            }
            if (mismatches > allowed || informative < 4) continue;

            int score = informative - 20 * mismatches;
            bool better = score > bestScore ||
                          (score == bestScore && _lastShift is int hint && best is int cur && Math.Abs(dy - hint) < Math.Abs(cur - hint));
            if (better)
            {
                bestScore = score;
                best = dy;
            }
        }
        return best;
    }
}
