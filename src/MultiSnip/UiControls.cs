namespace MultiSnip;

/// <summary>
/// WinForms tool bars swallow the first click when the window isn't active (the click only activates the
/// window), so buttons seemed to need two clicks. These versions act on the first click.
/// </summary>
internal static class ClickThrough
{
    private const int WM_MOUSEACTIVATE = 0x21;
    private const int MA_ACTIVATE = 1;
    private const int MA_ACTIVATEANDEAT = 2;

    public static void Fix(ref Message m)
    {
        if (m.Msg == WM_MOUSEACTIVATE && m.Result == (IntPtr)MA_ACTIVATEANDEAT)
            m.Result = (IntPtr)MA_ACTIVATE;
    }
}

internal sealed class ClickThroughToolStrip : ToolStrip
{
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        ClickThrough.Fix(ref m);
    }
}

internal sealed class ClickThroughMenuStrip : MenuStrip
{
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        ClickThrough.Fix(ref m);
    }
}

internal sealed class ClickThroughStatusStrip : StatusStrip
{
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        ClickThrough.Fix(ref m);
    }
}

/// <summary>
/// Large, high-contrast tabs: the active tab is blue with white bold text, others are grey, each has a × to close,
/// and a + at the end opens a new tab. Middle-click closes, right-click shows the tab menu.
/// </summary>
internal sealed class TabStrip : Control
{
    private static readonly Color StripBack = Color.FromArgb(43, 43, 48);
    private static readonly Color TabBack = Color.FromArgb(78, 78, 86);
    private static readonly Color TabHover = Color.FromArgb(105, 105, 115);
    private static readonly Color Active = Color.FromArgb(0, 103, 192);

    private readonly List<(string Text, string Tip)> _tabs = new();
    private readonly List<Rectangle> _tabRects = new();
    private readonly List<Rectangle> _closeRects = new();
    private Rectangle _plusRect;
    private int _selected = -1;
    private int _hover = -1;
    private bool _hoverClose, _hoverPlus;
    private readonly ToolTip _tip = new();
    private string? _currentTip;

    public TabStrip()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = StripBack;
        Dock = DockStyle.Top;
    }

    public event EventHandler<int>? TabClicked;
    public event EventHandler<int>? TabCloseClicked;
    public event EventHandler? NewTabClicked;
    public event EventHandler<(int Index, Point Location)>? TabRightClicked;

    public void SetTabs(IEnumerable<(string Text, string Tip)> tabs, int selected)
    {
        _tabs.Clear();
        _tabs.AddRange(tabs);
        _selected = selected;
        UpdateHeight();
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        UpdateHeight();
    }

    private Font BoldFont => new(Font, FontStyle.Bold);

    private void UpdateHeight() => Height = Font.Height + LogicalToDeviceUnits(18);

    private void Measure(Graphics g)
    {
        _tabRects.Clear();
        _closeRects.Clear();
        int pad = LogicalToDeviceUnits(14), gap = LogicalToDeviceUnits(4), top = LogicalToDeviceUnits(5);
        int closeSize = Font.Height;
        int x = LogicalToDeviceUnits(6);
        using Font bold = BoldFont;
        for (int i = 0; i < _tabs.Count; i++)
        {
            int textW = TextRenderer.MeasureText(g, _tabs[i].Text, bold).Width;
            int w = Math.Min(LogicalToDeviceUnits(320), pad + textW + gap + closeSize + pad / 2);
            var r = new Rectangle(x, top, w, Height - top);
            _tabRects.Add(r);
            _closeRects.Add(new Rectangle(r.Right - pad / 2 - closeSize, r.Top + (r.Height - closeSize) / 2, closeSize, closeSize));
            x += w + gap;
        }
        int plus = Height - top - LogicalToDeviceUnits(4);
        _plusRect = new Rectangle(x + gap, top + LogicalToDeviceUnits(2), plus + LogicalToDeviceUnits(8), plus);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(StripBack);
        Measure(g);
        using Font bold = BoldFont;
        for (int i = 0; i < _tabs.Count; i++)
        {
            Rectangle r = _tabRects[i];
            bool active = i == _selected;
            Color back = active ? Active : i == _hover ? TabHover : TabBack;
            using (var b = new SolidBrush(back)) g.FillRectangle(b, r);
            if (active)
            {
                using var accent = new SolidBrush(Color.FromArgb(255, 200, 0));
                g.FillRectangle(accent, r.X, r.Y, r.Width, LogicalToDeviceUnits(3));
            }
            var textRect = new Rectangle(r.X + LogicalToDeviceUnits(12), r.Y, _closeRects[i].Left - r.X - LogicalToDeviceUnits(14), r.Height);
            TextRenderer.DrawText(g, _tabs[i].Text, active ? bold : Font, textRect, Color.White,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            Rectangle c = _closeRects[i];
            if (i == _hover && _hoverClose)
                using (var hb = new SolidBrush(Color.FromArgb(200, 50, 50))) g.FillRectangle(hb, c);
            TextRenderer.DrawText(g, "×", Font, c, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        using (var pb = new SolidBrush(_hoverPlus ? TabHover : TabBack)) g.FillRectangle(pb, _plusRect);
        TextRenderer.DrawText(g, "+", bold, _plusRect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private int TabAt(Point p) => _tabRects.FindIndex(r => r.Contains(p));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hover = TabAt(e.Location);
        bool hoverClose = hover >= 0 && _closeRects[hover].Contains(e.Location);
        bool hoverPlus = _plusRect.Contains(e.Location);
        if (hover != _hover || hoverClose != _hoverClose || hoverPlus != _hoverPlus)
        {
            _hover = hover;
            _hoverClose = hoverClose;
            _hoverPlus = hoverPlus;
            Invalidate();
        }
        string? tip = hoverPlus ? "New tab (Ctrl+T)" : hoverClose ? "Close tab (Ctrl+W)" : hover >= 0 ? _tabs[hover].Tip : null;
        if (tip != _currentTip)
        {
            _currentTip = tip;
            _tip.SetToolTip(this, tip);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        _hoverClose = _hoverPlus = false;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_plusRect.Contains(e.Location) && e.Button == MouseButtons.Left)
        {
            NewTabClicked?.Invoke(this, EventArgs.Empty);
            return;
        }
        int i = TabAt(e.Location);
        if (i < 0) return;
        if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && _closeRects[i].Contains(e.Location)))
            TabCloseClicked?.Invoke(this, i);
        else if (e.Button == MouseButtons.Left)
            TabClicked?.Invoke(this, i);
        else if (e.Button == MouseButtons.Right)
            TabRightClicked?.Invoke(this, (i, e.Location));
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        ClickThrough.Fix(ref m);
    }
}
