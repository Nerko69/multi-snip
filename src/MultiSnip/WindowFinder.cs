using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MultiSnip;

/// <summary>
/// Remembers where the visible windows and their inner parts (e.g. a browser's page area) are, so the snip overlay
/// can highlight the one under the mouse and capture it with a single click.
/// </summary>
public sealed class WindowFinder
{
    private sealed record Win(Rectangle Bounds, List<Rectangle> Children);

    private readonly List<Win> _windows = new(); // top-most first

    private WindowFinder() { }

    /// <summary>Takes a snapshot of the windows on screen (call before showing the overlay).</summary>
    public static WindowFinder Capture()
    {
        var f = new WindowFinder();
        try
        {
            int ourPid = Environment.ProcessId;
            EnumWindows((hwnd, _) =>
            {
                if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || IsCloaked(hwnd)) return true;
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == ourPid) return true;
                long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
                if ((ex & WS_EX_TRANSPARENT) != 0 && (ex & WS_EX_LAYERED) != 0) return true; // click-through overlays
                Rectangle bounds = FrameBounds(hwnd);
                if (bounds.Width < 20 || bounds.Height < 20) return true;
                var children = new List<Rectangle>();
                EnumChildWindows(hwnd, (child, _) =>
                {
                    if (IsWindowVisible(child) && GetWindowRect(child, out RECT r))
                    {
                        Rectangle cr = Rectangle.Intersect(r.ToRectangle(), bounds);
                        if (cr.Width >= 40 && cr.Height >= 40) children.Add(cr);
                    }
                    return true;
                }, IntPtr.Zero);
                f._windows.Add(new Win(bounds, children));
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex); // window picking is a convenience; dragging still works
        }
        return f;
    }

    /// <summary>The smallest window part under <paramref name="screenPoint"/> (screen pixels), or empty.</summary>
    public Rectangle At(Point screenPoint)
    {
        foreach (Win w in _windows)
        {
            if (!w.Bounds.Contains(screenPoint)) continue;
            Rectangle best = w.Bounds;
            long bestArea = (long)best.Width * best.Height;
            foreach (Rectangle c in w.Children)
            {
                long a = (long)c.Width * c.Height;
                if (c.Contains(screenPoint) && a < bestArea)
                {
                    best = c;
                    bestArea = a;
                }
            }
            return best;
        }
        return Rectangle.Empty;
    }

    private static Rectangle FrameBounds(IntPtr hwnd)
    {
        // Without the invisible resize borders Windows 10/11 adds around windows.
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, Marshal.SizeOf<RECT>()) == 0)
            return r.ToRectangle();
        return GetWindowRect(hwnd, out RECT wr) ? wr.ToRectangle() : Rectangle.Empty;
    }

    private static bool IsCloaked(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
        public Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
}
