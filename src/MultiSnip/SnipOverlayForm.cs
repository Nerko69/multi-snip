using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MultiSnip;

/// <summary>
/// Full-screen overlay (spanning all monitors) showing a frozen screenshot.
/// The user drags a rectangle to snip it. Esc / right-click cancels,
/// Enter or double-click snips the whole screen under the cursor.
/// </summary>
public sealed class SnipOverlayForm : Form
{
    private readonly Bitmap _screen;
    private readonly Rectangle _virtual;
    private Point _start;
    private Point _current;
    private bool _dragging;
    private List<Rectangle> _lastDirty = new();
    private readonly Bitmap _dimmed;
    private readonly Font _labelFont = new("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);

    public Bitmap? Result { get; private set; }

    public SnipOverlayForm(Bitmap screen, Rectangle virtualScreen, string hint)
    {
        _screen = screen;
        _virtual = virtualScreen;
        Hint = hint;

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Bounds = virtualScreen;

        // Pre-render the darkened background once; painting is then two cheap blits.
        _dimmed = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(_dimmed);
        g.DrawImageUnscaled(screen, 0, 0);
        using var dim = new SolidBrush(Color.FromArgb(110, 0, 0, 0));
        g.FillRectangle(dim, 0, 0, screen.Width, screen.Height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dimmed.Dispose();
            _labelFont.Dispose();
        }
        base.Dispose(disposing);
    }

    public string Hint { get; }

    /// <summary>Captures every monitor into a single bitmap (physical pixels).</summary>
    public static Bitmap CaptureVirtualScreen(out Rectangle bounds)
    {
        bounds = SystemInformation.VirtualScreen;
        var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Bounds = _virtual; // re-apply in case the window manager moved us
        Activate();
        Focus();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        // The overlay spans monitors with different scaling; keep it pixel-exact instead of rescaling.
        e.Cancel = true;
        Bounds = _virtual;
    }

    private Rectangle Selection => Rectangle.FromLTRB(
        Math.Min(_start.X, _current.X), Math.Min(_start.Y, _current.Y),
        Math.Max(_start.X, _current.X), Math.Max(_start.Y, _current.Y));

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            Cancel();
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        if (e.Clicks >= 2)
        {
            SnipScreenAt(e.Location);
            return;
        }
        _dragging = true;
        _start = _current = e.Location;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_current == e.Location) return;
        _current = e.Location;
        InvalidateDirty();
    }

    /// <summary>Repaints only what changed: old and new selection, label and crosshair.</summary>
    private void InvalidateDirty()
    {
        var dirty = new List<Rectangle>();
        if (_dragging)
        {
            Rectangle sel = Selection;
            sel.Inflate(3, 3);
            dirty.Add(sel);
            dirty.Add(LabelRect(SizeText, new Point(Selection.Left, Selection.Bottom + 6)));
        }
        else
        {
            dirty.Add(new Rectangle(0, _current.Y - 1, Width, 3));
            dirty.Add(new Rectangle(_current.X - 1, 0, 3, Height));
            dirty.Add(LabelRect(Hint, new Point(_current.X + 16, _current.Y + 16)));
        }
        foreach (Rectangle r in _lastDirty) Invalidate(r);
        foreach (Rectangle r in dirty) Invalidate(r);
        _lastDirty = dirty;
    }

    private string SizeText => $"{Selection.Width} × {Selection.Height}";

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!_dragging || e.Button != MouseButtons.Left) return;
        _dragging = false;
        _current = e.Location;
        Rectangle sel = Rectangle.Intersect(Selection, new Rectangle(Point.Empty, _screen.Size));
        if (sel.Width < 3 || sel.Height < 3)
        {
            Invalidate();
            return; // treat as an accidental click
        }
        Finish(sel);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
            Cancel();
        else if (e.KeyCode == Keys.Enter)
            SnipScreenAt(PointToClient(Cursor.Position));
    }

    private void SnipScreenAt(Point clientPoint)
    {
        Point screenPoint = new(clientPoint.X + _virtual.X, clientPoint.Y + _virtual.Y);
        Rectangle b = Screen.FromPoint(screenPoint).Bounds;
        b.Offset(-_virtual.X, -_virtual.Y);
        Finish(Rectangle.Intersect(b, new Rectangle(Point.Empty, _screen.Size)));
    }

    private void Finish(Rectangle sel)
    {
        Result = _screen.Clone(sel, PixelFormat.Format32bppArgb);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void Cancel()
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    protected override void OnPaintBackground(PaintEventArgs e) { /* everything is painted in OnPaint */ }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        Rectangle clip = e.ClipRectangle;

        g.DrawImage(_dimmed, clip, clip, GraphicsUnit.Pixel);

        if (_dragging)
        {
            Rectangle sel = Selection;
            g.DrawImage(_screen, sel, sel, GraphicsUnit.Pixel);
            using var pen = new Pen(Color.FromArgb(0, 120, 215), 2);
            g.DrawRectangle(pen, sel);
            DrawLabel(g, SizeText, new Point(sel.Left, sel.Bottom + 6));
        }
        else
        {
            using var pen = new Pen(Color.FromArgb(160, 255, 255, 255), 1) { DashStyle = DashStyle.Dash };
            g.DrawLine(pen, 0, _current.Y, Width, _current.Y);
            g.DrawLine(pen, _current.X, 0, _current.X, Height);
            DrawLabel(g, Hint, new Point(_current.X + 16, _current.Y + 16));
        }
    }

    private Rectangle LabelRect(string text, Point at)
    {
        Size sz = TextRenderer.MeasureText(text, _labelFont);
        var rect = new Rectangle(at, new Size(sz.Width + 8, sz.Height + 4));
        // keep the label on-screen
        if (rect.Right > Width) rect.X = Math.Max(0, Width - rect.Width - 4);
        if (rect.Bottom > Height) rect.Y = Math.Max(0, at.Y - rect.Height - 40);
        return rect;
    }

    private void DrawLabel(Graphics g, string text, Point at)
    {
        Rectangle rect = LabelRect(text, at);
        using var bg = new SolidBrush(Color.FromArgb(220, 32, 32, 32));
        g.FillRectangle(bg, rect);
        TextRenderer.DrawText(g, text, _labelFont, rect, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
