using System.Drawing.Imaging;

namespace MultiSnip;

/// <summary>
/// "MultiSnip.exe --self-test [log file]": exercises drawing, layout, editable-PNG save/open, undo and the main
/// window on a real Windows machine without any user input. Exit code = number of failures. Run by the CI build.
/// </summary>
internal static class SelfTest
{
    private static readonly List<string> Log = new();
    private static int _failures;

    private static void Check(string name, bool ok, string detail = "")
    {
        Log.Add($"{(ok ? "PASS" : "FAIL")}  {name} {detail}");
        if (!ok) _failures++;
    }

    public static int Run(string? logPath)
    {
        // Never touch the real user's settings or open tabs.
        Settings.AppDataOverride = Path.Combine(Path.GetTempPath(), "MultiSnip-selftest", "AppData");
        try
        {
            RenderingTests();
            HitTests();
            SaveOpenTests();
            UndoTests();
            MainWindowSmokeTest();
        }
        catch (Exception ex)
        {
            Check("unexpected exception", false, ex.ToString());
        }
        Log.Add(_failures == 0 ? "All self-tests passed." : $"{_failures} self-test(s) failed.");
        File.WriteAllLines(logPath ?? Path.Combine(Path.GetTempPath(), "MultiSnip-selftest.log"), Log);
        return _failures;
    }

    private static Bitmap Solid(int w, int h, Color c)
    {
        var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(b);
        g.Clear(c);
        return b;
    }

    private static Project SampleProject()
    {
        var p = new Project(new CombineOptions { Layout = CombineLayout.Vertical, Spacing = 10, Background = Color.White });
        p.Snips.Add(new SnipItem(Solid(200, 100, Color.LightGray), "test"));
        p.Snips.Add(new SnipItem(Solid(120, 80, Color.LightBlue), "test"));
        p.Snips.Add(new SnipItem(Solid(160, 60, Color.LightGreen), "test"));
        SnipItem s = p.Snips[1];
        s.Annotations.Add(new Annotation { Kind = AnnotationKind.Rectangle, Filled = true, Argb = Color.Red.ToArgb(), Points = { new PointF(10, 10), new PointF(30, 30) } });
        s.Annotations.Add(new Annotation { Kind = AnnotationKind.Pen, Argb = Color.Black.ToArgb(), Width = 3, Points = { new PointF(40, 10), new PointF(60, 40), new PointF(80, 20) } });
        s.Annotations.Add(new Annotation { Kind = AnnotationKind.Marker, Argb = Color.Yellow.ToArgb(), Width = 12, Points = { new PointF(5, 60), new PointF(110, 60) } });
        s.Annotations.Add(new Annotation { Kind = AnnotationKind.Arrow, Argb = Color.Blue.ToArgb(), Width = 3, Points = { new PointF(5, 75), new PointF(100, 75) } });
        p.Snips[0].Annotations.Add(new Annotation { Kind = AnnotationKind.Text, Text = "Step one", FontSize = 18, Argb = Color.DarkRed.ToArgb(), Points = { new PointF(10, 10) } });
        p.Snips[0].Annotations.Add(new Annotation { Kind = AnnotationKind.BadgeCircle, Text = "1", FontSize = 16, Argb = Color.Red.ToArgb(), Points = { new PointF(170, 50) } });
        p.Snips[2].Annotations.Add(new Annotation { Kind = AnnotationKind.BadgeSquare, Text = "2", FontSize = 16, Argb = Color.Purple.ToArgb(), Points = { new PointF(30, 30) } });
        p.Snips[2].Annotations.Add(new Annotation { Kind = AnnotationKind.Ellipse, Argb = Color.Green.ToArgb(), Width = 2, Points = { new PointF(60, 10), new PointF(140, 50) } });
        p.Snips[2].Annotations.Add(new Annotation { Kind = AnnotationKind.Line, Argb = Color.Black.ToArgb(), Width = 2, Points = { new PointF(0, 55), new PointF(150, 55) } });
        p.NextBadgeNumber = 3;
        return p;
    }

    private static void RenderingTests()
    {
        Project p = SampleProject();
        foreach (CombineLayout layout in Enum.GetValues<CombineLayout>())
        {
            p.Options.Layout = layout;
            p.Options.GridColumns = 0;
            CombinedLayout l = p.ComputeLayout();
            using Bitmap final = p.RenderFinal();
            Check($"render {layout}: size matches layout", final.Size == l.Total, $"{final.Size} vs {l.Total}");

            // The filled red rectangle on snip 2 (snip pixels 10..30) must land inside that snip's place.
            Rectangle r = l.ImageRects[1];
            Color c = final.GetPixel(r.X + 20, r.Y + 20);
            Check($"render {layout}: drawing lands on its snip", c.R > 200 && c.G < 60 && c.B < 60, c.ToString());
            Color bg = final.GetPixel(r.X + 115, r.Y + 5);
            Check($"render {layout}: snip pixels kept", bg.ToArgb() == Color.LightBlue.ToArgb(), bg.ToString());
        }

        // "Match sizes" scales snips; drawings must scale with them.
        p.Options.Layout = CombineLayout.Vertical;
        p.Options.MatchSize = true;
        CombinedLayout ml = p.ComputeLayout();
        using (Bitmap m = p.RenderFinal())
        {
            Rectangle r = ml.ImageRects[1];
            float k = r.Width / 120f;
            Color c = m.GetPixel(r.X + (int)(20 * k), r.Y + (int)(20 * k));
            Check("match sizes: drawing scales with its snip", c.R > 200 && c.G < 60, $"k={k:0.00} {c}");
        }
        p.Options.MatchSize = false;

        p.Options.OutputScale = 50;
        CombinedLayout sl = p.ComputeLayout();
        using (Bitmap half = p.RenderFinal())
            Check("output size 50%", half.Width == sl.Total.Width / 2 && half.Height == sl.Total.Height / 2, half.Size.ToString());
        p.Options.OutputScale = 100;

        // Many snips in an automatic grid.
        var big = new Project(new CombineOptions { Layout = CombineLayout.Grid, GridColumns = 0, Spacing = 0 });
        for (int i = 0; i < 25; i++) big.Snips.Add(new SnipItem(Solid(40, 30, Color.FromArgb(i * 10, 100, 200)), "test"));
        using (Bitmap g = big.RenderFinal())
            Check("auto grid 25 snips = 5 × 5", g.Width == 200 && g.Height == 150, g.Size.ToString());
    }

    private static void HitTests()
    {
        Project p = SampleProject();
        List<Annotation> a1 = p.Snips[1].Annotations;
        Check("hit: filled rectangle inside", AnnotationRenderer.HitTest(a1[0], new PointF(20, 20), 2));
        Check("hit: pen stroke on line", AnnotationRenderer.HitTest(a1[1], new PointF(50, 25), 2));
        Check("hit: pen stroke miss", !AnnotationRenderer.HitTest(a1[1], new PointF(10, 70), 2));
        Check("hit: arrow on line", AnnotationRenderer.HitTest(a1[3], new PointF(50, 75), 2));
        List<Annotation> a0 = p.Snips[0].Annotations;
        Check("hit: text box", AnnotationRenderer.HitTest(a0[0], new PointF(20, 18), 2));
        Check("hit: badge", AnnotationRenderer.HitTest(a0[1], new PointF(170, 50), 2));
        List<Annotation> a2 = p.Snips[2].Annotations;
        Check("hit: outline ellipse edge", AnnotationRenderer.HitTest(a2[1], new PointF(60, 30), 3));
        Check("hit: outline ellipse centre misses", !AnnotationRenderer.HitTest(a2[1], new PointF(100, 30), 3));
    }

    private static void SaveOpenTests()
    {
        string dir = Path.Combine(Path.GetTempPath(), "MultiSnip-selftest");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, FileNames.WithTimestamp("SelfTest", DateTime.Now) + ".png");
        Project p = SampleProject();
        p.Options.Layout = CombineLayout.Grid;
        p.Options.GridColumns = 2;
        ProjectFile.Save(p, path);

        using (var img = Image.FromFile(path))
            Check("saved file is a normal PNG image", img.Size == p.ComputeLayout().Total, img.Size.ToString());

        Project? back = ProjectFile.TryOpen(path);
        Check("saved file reopens as a project", back != null);
        if (back == null) return;
        Check("reopen: snips", back.Snips.Count == 3 && back.Snips[1].Image.Size == new Size(120, 80));
        Check("reopen: drawings", back.Snips.Sum(s => s.Annotations.Count) == p.Snips.Sum(s => s.Annotations.Count));
        Check("reopen: options", back.Options.Layout == CombineLayout.Grid && back.Options.GridColumns == 2);
        Check("reopen: next label number", back.NextBadgeNumber == 3 && back.SuggestNextBadge() == 3);
        Check("reopen: original pixels", back.Snips[2].Image.GetPixel(5, 5).ToArgb() == Color.LightGreen.ToArgb());

        using Bitmap a = p.RenderFinal(), b = back.RenderFinal();
        bool same = a.Size == b.Size;
        for (int y = 0; same && y < a.Height; y += 3)
            for (int x = 0; same && x < a.Width; x += 3)
                same = a.GetPixel(x, y) == b.GetPixel(x, y);
        Check("reopened project renders identically", same);

        // Rearranging after reopening still works on the originals.
        (back.Snips[0], back.Snips[2]) = (back.Snips[2], back.Snips[0]);
        using Bitmap c = back.RenderFinal();
        Check("reopened snips can be rearranged", c.Size == back.ComputeLayout().Total);

        Check("plain PNG is not a project", ProjectFile.TryOpen(SavePlain(dir)) == null);
    }

    private static string SavePlain(string dir)
    {
        string path = Path.Combine(dir, "plain.png");
        using Bitmap b = Solid(10, 10, Color.Red);
        b.Save(path, ImageFormat.Png);
        return path;
    }

    private static void UndoTests()
    {
        Project p = SampleProject();
        int before = p.Snips[1].Annotations.Count;
        p.Checkpoint();
        p.Snips[1].Annotations.RemoveAt(0);
        p.Checkpoint();
        p.Snips.RemoveAt(0);
        Check("undo: removing a snip undone", p.Undo() && p.Snips.Count == 3);
        Check("undo: removing a drawing undone", p.Undo() && p.Snips[1].Annotations.Count == before);
        Check("redo works", p.Redo() && p.Snips[1].Annotations.Count == before - 1);
        Check("second redo removes the snip again", p.Redo() && p.Snips.Count == 2 && !p.CanRedo);
    }

    private static void MainWindowSmokeTest()
    {
        Exception? uiError = null;
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => uiError ??= e.Exception;
        using var form = new MainForm();
        var timer = new System.Windows.Forms.Timer { Interval = 1500 };
        timer.Tick += (_, _) => { timer.Stop(); form.Close(); };
        form.Shown += (_, _) => timer.Start();
        Application.Run(form);
        Check("main window opens, lays out, paints and closes", uiError == null, uiError?.ToString() ?? "");
    }
}
