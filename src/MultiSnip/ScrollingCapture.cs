using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MultiSnip;

/// <summary>
/// Captures a scrollable area (web page, document, chat...) beyond what is visible:
/// scrolls it with the mouse wheel, captures each step and stitches the frames together.
/// The user can press Esc at any time to stop and keep what was captured so far.
/// </summary>
internal static class ScrollingCapture
{
    private const int WheelNotch = 120;
    private const int MaxSteps = 400;

    public sealed record Outcome(Bitmap Image, int Frames, bool StoppedByUser, bool HitLimit, int UnmatchedSteps);

    public static async Task<Outcome> RunAsync(Rectangle area, bool scrollToTopFirst)
    {
        Point oldCursor = Cursor.Position;
        // Park the cursor inside the area (wheel input goes to the window under it), near the right
        // edge so hover effects on the page content are unlikely.
        Cursor.Position = new Point(area.Right - Math.Clamp(area.Width / 10, 4, 40), area.Top + area.Height / 2);
        NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE); // reset the "pressed since last call" bit
        bool stopped = false, hitLimit = false;
        int unmatched = 0;
        try
        {
            if (scrollToTopFirst)
            {
                int[]? last = null;
                for (int i = 0; i < 80 && !(stopped = EscPressed()); i++)
                {
                    NativeMethods.MouseWheel(15 * WheelNotch);
                    await Task.Delay(120);
                    int[] now = Grab(area);
                    if (last != null && now.AsSpan().SequenceEqual(last)) break;
                    last = now;
                }
            }

            var stitcher = new ScrollStitcher(area.Width, area.Height, maxHeight: 20000);
            stitcher.Add(await GrabStableAsync(area));
            for (int step = 0; step < MaxSteps && !stopped; step++)
            {
                if (stopped = EscPressed()) break;
                NativeMethods.MouseWheel(-3 * WheelNotch);
                var result = stitcher.Add(await GrabStableAsync(area));
                if (result == ScrollStitcher.StepResult.NoChange)
                {
                    // Maybe still loading more content (infinite scroll / lazy loading): give it a moment.
                    await Task.Delay(600);
                    result = stitcher.Add(await GrabStableAsync(area));
                    if (result == ScrollStitcher.StepResult.NoChange) break; // end of the page
                }
                if (result == ScrollStitcher.StepResult.NoMatch) unmatched++;
                if (result == ScrollStitcher.StepResult.LimitReached) { hitLimit = true; break; }
            }

            int[] px = stitcher.Build(out int height);
            return new Outcome(ToBitmap(px, area.Width, height), stitcher.Frames, stopped, hitLimit, unmatched);
        }
        finally
        {
            Cursor.Position = oldCursor;
        }
    }

    private static bool EscPressed() => (NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8001) != 0;

    /// <summary>Waits for smooth-scrolling animations to finish: grabs until two captures are identical.</summary>
    private static async Task<int[]> GrabStableAsync(Rectangle area)
    {
        await Task.Delay(150);
        int[] prev = Grab(area);
        for (int i = 0; i < 12; i++)
        {
            await Task.Delay(90);
            int[] cur = Grab(area);
            if (cur.AsSpan().SequenceEqual(prev)) return cur;
            prev = cur;
        }
        return prev; // keeps changing (animation/video): use the latest
    }

    private static int[] Grab(Rectangle area)
    {
        using var bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(area.Location, Point.Empty, area.Size, CopyPixelOperation.SourceCopy);
        var data = bmp.LockBits(new Rectangle(Point.Empty, area.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var px = new int[area.Width * area.Height];
            for (int y = 0; y < area.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, px, y * area.Width, area.Width);
            return px;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private static Bitmap ToBitmap(int[] px, int width, int height)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < height; y++)
                Marshal.Copy(px, y * width, data.Scan0 + y * data.Stride, width);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }
}
