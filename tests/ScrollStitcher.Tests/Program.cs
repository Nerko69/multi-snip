// Simulates scrolling a synthetic page (sticky header/footer, moving scrollbar, blank gaps)
// and checks that the stitched result equals the original page pixel for pixel.
using MultiSnip;
int failures = 0;
void Check(string name, bool ok, string extra = "") { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name} {extra}"); if (!ok) failures++; }

// Page content: W x pageH. Rows: text-like random rows with blank gaps.
int[] MakePage(int w, int h, int seed, bool repetitiveBlank)
{
    var rnd = new Random(seed); var p = new int[w * h];
    for (int y = 0; y < h; y++)
    {
        bool blank = repetitiveBlank ? (y / 40) % 3 == 0 : rnd.NextDouble() < 0.3;
        for (int x = 0; x < w; x++) p[y * w + x] = blank ? -1 : (x % 7 == 0 ? rnd.Next() : unchecked((int)0xFF000000) | (y * 31 + x));
    }
    return p;
}

void Run(string name, int w, int vh, int pageH, int header, int footer, int[] steps, bool changingHeader = false, int seed = 1, bool repetitive=false)
{
    int[] page = MakePage(w, pageH, seed, repetitive);
    int contentH = vh - header - footer;
    int maxOff = pageH - contentH;
    int sbW = 20;
    int[] Frame(int off, int idx)
    {
        var f = new int[w * vh];
        for (int y = 0; y < vh; y++)
            for (int x = 0; x < w; x++)
            {
                int v;
                if (y < header) v = changingHeader && idx > 0 && y == header - 1 ? 0x123456 : unchecked((int)0xFF2040A0) + x;
                else if (y >= vh - footer) v = unchecked((int)0xFF00A000) + x * 3 + y;
                else v = page[(off + y - header) * w + x];
                if (x >= w - sbW) v = (y >= off * vh / pageH && y < off * vh / pageH + 50) ? 0x777777 : 0xDDDDDD; // scrollbar
                f[y * w + x] = v;
            }
        return f;
    }
    var st = new ScrollStitcher(w, vh);
    int offset = 0, i = 0, noMatch = 0;
    st.Add(Frame(0, 0));
    foreach (int s in steps.Concat(Enumerable.Repeat(steps.Last(), 1000)))
    {
        offset = Math.Min(maxOff, offset + s);
        var r = st.Add(Frame(offset, ++i));
        if (r == ScrollStitcher.StepResult.NoMatch) noMatch++;
        if (r == ScrollStitcher.StepResult.NoChange) break;
    }
    int[] outPx = st.Build(out int outH);
    int expectedH = header + pageH + footer;
    // Compare excluding scrollbar strip and (if changing header) the header row.
    int bad = 0;
    if (outH == expectedH)
        for (int y = 0; y < outH; y++)
            for (int x = 0; x < w - sbW; x++)
            {
                int exp = y < header ? unchecked((int)0xFF2040A0) + x
                        : y >= header + pageH ? unchecked((int)0xFF00A000) + x * 3 + (y - header - pageH + vh - footer)
                        : page[(y - header) * w + x];
                if (outPx[y * w + x] != exp) bad++;
            }
    Check(name, outH == expectedH && bad == 0, $"(height {outH} vs {expectedH}, bad px {bad}, frames {st.Frames}, nomatch {noMatch})");
}

Run("plain page, steady scroll", 300, 400, 3000, 0, 0, new[] { 120 });
Run("sticky header+footer", 300, 400, 2500, 60, 30, new[] { 100 });
Run("uneven scroll steps", 320, 500, 4000, 40, 0, new[] { 90, 150, 33, 200, 7, 120 });
Run("repetitive blank bands", 300, 400, 2600, 50, 20, new[] { 110 }, repetitive: true);
Run("header changes after first scroll", 300, 400, 2200, 60, 0, new[] { 130 }, changingHeader: true);
Run("page shorter than 2 screens", 300, 400, 500, 30, 0, new[] { 300 });

// Single frame, no scroll
{
    var st = new ScrollStitcher(10, 10); var px = Enumerable.Range(0, 100).ToArray();
    st.Add(px); var r = st.Add((int[])px.Clone());
    var o = st.Build(out int h);
    Check("non-scrolling area returns first frame", r == ScrollStitcher.StepResult.NoChange && h == 10 && o.SequenceEqual(px));
}
return failures;
