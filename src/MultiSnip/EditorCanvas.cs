using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MultiSnip;

public enum DrawTool { Select, Area, Pen, Marker, Eraser, Line, Arrow, Rectangle, Ellipse, Text, BadgeCircle, BadgeSquare }

/// <summary>
/// Shows the combined image at any zoom and lets the user draw on it like in Paint. Drawings are stored as
/// editable objects on the snip under the mouse (in that snip's own pixels), so they move with the snip.
/// </summary>
public sealed class EditorCanvas : ScrollableControl
{
    public const float MinZoom = 0.1f, MaxZoom = 8f;
    private const int Pad = 16;

    private Project? _project;
    private IReadOnlyList<SnipItem>? _snips;   // what is shown: all snips, or a single one
    private bool _overlayEditing;               // overlay layout: snips 2..n can be moved/resized

    // area selection (Area tool), in snip pixels
    private (int Snip, Rectangle Rect)? _area;
    private bool _areaDragging;
    private PointF _areaStart;

    // overlay move / resize
    private enum OverlayDrag { None, Move, TopLeft, TopRight, BottomLeft, BottomRight }
    private int _overlaySel = -1;
    private OverlayDrag _overlayDrag;
    private bool _overlayCheckpointed;
    private PointF _overlayStartMouse;
    private RectangleF _overlayStartRect;
    private Bitmap? _base;
    private CombinedLayout? _layout;
    private Bitmap? _scaled;          // cache of the base image drawn below 100% zoom
    private float _scaledZoom;
    private DrawTool _tool = DrawTool.Select;

    // in-progress operations
    private Annotation? _draft;
    private int _draftSnip = -1;
    private (int Snip, Annotation A)? _selected;
    private bool _moving, _moveCheckpointed;
    private PointF _moveLast;
    private bool _erasing, _eraseCheckpointed;
    private bool _panning;
    private Point _panStart, _panScrollStart;

    // in-place text editor
    private readonly TextBox _editor = new() { Multiline = true, BorderStyle = BorderStyle.FixedSingle, Visible = false, AcceptsReturn = true, WordWrap = false };
    private (int Snip, Annotation A, bool IsNew)? _editing;

    public EditorCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        AutoScroll = true;
        BackColor = Color.FromArgb(70, 70, 74);
        TabStop = true;
        Controls.Add(_editor);
        _editor.KeyDown += OnEditorKeyDown;
        _editor.TextChanged += (_, _) => SizeEditor();
        _editor.LostFocus += (_, _) => CommitText(refocus: false); // the user clicked elsewhere: don't steal focus back
    }

    // ------------------------------------------------------------------ public API

    public Color DrawColor { get; set; } = Color.FromArgb(232, 17, 35);
    public float LineWidth { get; set; } = 3;
    public float FontSizePx { get; set; } = 24;
    public bool Filled { get; set; }
    public bool BoldText { get; set; } = true;
    public string EmptyText { get; set; } = "";

    /// <summary>Watermark previewed on top of the combined image (null when viewing a single snip).</summary>
    public WatermarkOptions? Watermark { get; set; }

    /// <summary>Raised after the drawings changed (the tab is now modified).</summary>
    public event EventHandler? DocumentChanged;
    public event EventHandler? ZoomChanged;
    public event EventHandler<int>? SnipClicked;
    public event EventHandler? SelectionChanged;

    /// <summary>The area selection changed (Area tool).</summary>
    public event EventHandler? AreaChanged;

    /// <summary>An overlay snip was moved or resized: the combined image must be re-rendered.</summary>
    public event EventHandler? OverlayChanged;

    /// <summary>Area tool selection: view index of the snip and the rectangle in its pixels.</summary>
    public (int Snip, Rectangle Rect)? AreaSelection => _area;

    public void ClearArea()
    {
        if (_area == null) return;
        _area = null;
        AreaChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public DrawTool Tool
    {
        get => _tool;
        set
        {
            CommitText();
            _tool = value;
            if (value != DrawTool.Select) { Select(null); _overlaySel = -1; }
            if (value != DrawTool.Area) ClearArea();
            Cursor = value switch
            {
                DrawTool.Select => Cursors.Default,
                DrawTool.Text => Cursors.IBeam,
                DrawTool.Eraser => Cursors.Hand,
                _ => Cursors.Cross,
            };
        }
    }

    public bool HasSelection => _selected != null;
    public Annotation? SelectedAnnotation => _selected?.A;

    /// <summary>
    /// Shows a tab: <paramref name="snips"/> are the snips on screen (all of them, or one) and <paramref name="layout"/>
    /// where they are. <paramref name="baseImage"/> (snips without drawings) is owned by the caller.
    /// </summary>
    public void SetDocument(Project? project, IReadOnlyList<SnipItem>? snips, Bitmap? baseImage, CombinedLayout? layout, bool overlayEditing)
    {
        bool sameView = ReferenceEquals(project, _project) && snips != null && _snips != null &&
                        snips.Count == _snips.Count && snips.Select((x, i) => ReferenceEquals(x, _snips[i])).All(b => b);
        if (!ReferenceEquals(project, _project)) CancelText();
        _project = project;
        _snips = snips;
        _base = baseImage;
        _layout = layout;
        _overlayEditing = overlayEditing;
        if (_overlayDrag == OverlayDrag.None) _draft = null; // keep an overlay drag going while we re-render
        if (!sameView)
        {
            Select(null);
            ClearArea();
            if (_overlayDrag == OverlayDrag.None) _overlaySel = -1;
        }
        else if (_selected is { } s && (s.Snip >= (snips?.Count ?? 0) || !snips![s.Snip].Annotations.Contains(s.A)))
        {
            Select(null);
        }
        if (!overlayEditing) _overlaySel = -1;
        InvalidateScaledCache();
        UpdateScrollSize();
        Invalidate();
    }

    public float FitZoom
    {
        get
        {
            if (_base == null) return 1;
            float zx = (ClientSize.Width - 2 * Pad) / (float)_base.Width;
            float zy = (ClientSize.Height - 2 * Pad) / (float)_base.Height;
            return Math.Clamp(Math.Min(Math.Min(zx, zy), 1f), MinZoom, 1f);
        }
    }

    public float Zoom => _project?.Zoom ?? FitZoom;
    public bool IsFit => _project?.Zoom == null;

    /// <summary>Sets the zoom (null = fit to window), keeping the point under <paramref name="anchor"/> in place.</summary>
    public void SetZoom(float? zoom, Point? anchor = null)
    {
        if (_project == null) return;
        CommitText();
        Point a = anchor ?? new Point(ClientSize.Width / 2, ClientSize.Height / 2);
        PointF imagePt = ClientToImage(a);
        _project.Zoom = zoom == null ? null : Math.Clamp(zoom.Value, MinZoom, MaxZoom);
        InvalidateScaledCache();
        UpdateScrollSize();
        if (zoom != null && _base != null)
        {
            // Scroll so that imagePt is under the anchor again.
            float z = Zoom;
            Point o0 = UnscrolledOrigin();
            AutoScrollPosition = new Point((int)(imagePt.X * z + o0.X - a.X), (int)(imagePt.Y * z + o0.Y - a.Y));
        }
        Invalidate();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ZoomBy(float factor, Point? anchor = null) => SetZoom(Zoom * factor, anchor);

    public void DeleteSelection()
    {
        if (_project == null || _selected is not { } s) return;
        _project.Checkpoint();
        _snips![s.Snip].Annotations.Remove(s.A);
        Select(null);
        RaiseChanged(s.Snip);
    }

    /// <summary>Applies the current toolbar style to the selected drawing (like Paint does while a shape is active).</summary>
    public void ApplyStyleToSelection(bool color = false, bool width = false, bool font = false, bool fill = false, bool bold = false)
    {
        if (_project == null || _selected is not { } s) return;
        _project.Checkpoint();
        Annotation a = s.A;
        if (color) a.Argb = DrawColor.ToArgb();
        if (width && !a.HasText) a.Width = LineWidth;
        if (font && a.HasText) a.FontSize = FontSizePx;
        if (fill) a.Filled = Filled;
        if (bold && a.Kind == AnnotationKind.Text) a.Bold = BoldText;
        RaiseChanged(s.Snip);
    }

    public void CommitPendingEdits() => CommitText();

    // ------------------------------------------------------------------ geometry

    private Size ContentSize => _base == null ? Size.Empty : new Size((int)Math.Ceiling(_base.Width * Zoom), (int)Math.Ceiling(_base.Height * Zoom));

    private void UpdateScrollSize()
    {
        Size c = ContentSize;
        AutoScrollMinSize = c.IsEmpty ? Size.Empty : new Size(c.Width + 2 * Pad, c.Height + 2 * Pad);
    }

    /// <summary>Top-left of the image when not scrolled (centred when smaller than the window).</summary>
    private Point UnscrolledOrigin()
    {
        Size c = ContentSize;
        return new Point(Math.Max(Pad, (ClientSize.Width - c.Width) / 2), Math.Max(Pad, (ClientSize.Height - c.Height) / 2));
    }

    private Point Origin()
    {
        Point o = UnscrolledOrigin();
        return new Point(o.X + AutoScrollPosition.X, o.Y + AutoScrollPosition.Y);
    }

    private PointF ClientToImage(Point p)
    {
        Point o = Origin();
        float z = Zoom;
        return new PointF((p.X - o.X) / z, (p.Y - o.Y) / z);
    }

    private float SnipScale(int i) => _layout!.ImageRects[i].Width / (float)_snips![i].Image.Width;

    private PointF ImageToSnip(PointF ip, int i)
    {
        Rectangle r = _layout!.ImageRects[i];
        Size img = _snips![i].Image.Size;
        return new PointF((ip.X - r.X) * img.Width / r.Width, (ip.Y - r.Y) * img.Height / r.Height);
    }

    private Point SnipToClient(PointF sp, int i)
    {
        Rectangle r = _layout!.ImageRects[i];
        Size img = _snips![i].Image.Size;
        Point o = Origin();
        float z = Zoom;
        return new Point((int)Math.Round(o.X + (r.X + sp.X * r.Width / img.Width) * z),
                         (int)Math.Round(o.Y + (r.Y + sp.Y * r.Height / img.Height) * z));
    }

    /// <summary>The snip under the point, or the nearest one (drawings may start in the gaps between snips).</summary>
    private int SnipAt(PointF ip)
    {
        if (_layout == null || _project == null || _snips == null || _snips.Count == 0) return -1;
        int best = -1;
        double bestDist = double.MaxValue;
        for (int i = 0; i < _layout.ImageRects.Length; i++)
        {
            Rectangle r = _layout.ImageRects[i];
            double dx = Math.Max(Math.Max(r.Left - ip.X, 0), ip.X - r.Right);
            double dy = Math.Max(Math.Max(r.Top - ip.Y, 0), ip.Y - r.Bottom);
            double d = dx * dx + dy * dy;
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    private (int Snip, Annotation A)? HitAnnotation(PointF ip, Func<Annotation, bool>? filter = null)
    {
        if (_project == null || _layout == null || _snips == null) return null;
        for (int i = _snips.Count - 1; i >= 0; i--)
        {
            List<Annotation> list = _snips[i].Annotations;
            if (list.Count == 0) continue;
            PointF sp = ImageToSnip(ip, i);
            float tol = 5f / (Zoom * SnipScale(i));
            for (int k = list.Count - 1; k >= 0; k--)
                if ((filter == null || filter(list[k])) && AnnotationRenderer.HitTest(list[k], sp, tol))
                    return (i, list[k]);
        }
        return null;
    }

    // ------------------------------------------------------------------ painting

    private void InvalidateScaledCache()
    {
        _scaled?.Dispose();
        _scaled = null;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (IsFit) InvalidateScaledCache();
        UpdateScrollSize();
        if (IsFit) ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);
        if (_base == null || _project == null || _layout == null || _snips == null)
        {
            TextRenderer.DrawText(g, EmptyText, Font, ClientRectangle, Color.Gainsboro,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        float z = Zoom;
        Point o = Origin();
        Size c = ContentSize;
        var dest = new Rectangle(o, c);
        using (var shadow = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
            g.FillRectangle(shadow, dest.X + 3, dest.Y + 3, dest.Width, dest.Height);
        if (_project.Options.TransparentBackground) DrawChecker(g, Rectangle.Intersect(dest, e.ClipRectangle));

        if (z < 0.999f)
        {
            if (_scaled == null || Math.Abs(_scaledZoom - z) > 0.0001f)
            {
                InvalidateScaledCache();
                _scaled = new Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height), PixelFormat.Format32bppPArgb);
                using Graphics sg = Graphics.FromImage(_scaled);
                sg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                sg.DrawImage(_base, new Rectangle(Point.Empty, c));
                _scaledZoom = z;
            }
            g.DrawImage(_scaled, o.X, o.Y, _scaled.Width, _scaled.Height);
        }
        else
        {
            // Draw only the visible part, pixel-exact.
            Rectangle visible = Rectangle.Intersect(dest, e.ClipRectangle);
            if (!visible.IsEmpty)
            {
                var src = RectangleF.FromLTRB((visible.Left - o.X) / z, (visible.Top - o.Y) / z, (visible.Right - o.X) / z, (visible.Bottom - o.Y) / z);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(_base, visible, src, GraphicsUnit.Pixel);
            }
        }

        // Drawings on top, transformed to screen.
        GraphicsState state = g.Save();
        g.TranslateTransform(o.X, o.Y);
        g.ScaleTransform(z, z);
        Project.DrawAnnotations(g, _layout, _snips!);
        if (Watermark != null) WatermarkRenderer.Draw(g, _base.Size, Watermark);
        if (_draft != null && _draftSnip >= 0)
        {
            GraphicsState s2 = g.Save();
            Project.ApplySnipTransform(g, _layout, _draftSnip, _snips![_draftSnip].Image.Size);
            AnnotationRenderer.Draw(g, _draft);
            g.Restore(s2);
        }
        if (_selected is { } sel && sel.Snip < _snips.Count)
        {
            GraphicsState s3 = g.Save();
            Project.ApplySnipTransform(g, _layout, sel.Snip, _snips![sel.Snip].Image.Size);
            RectangleF b = AnnotationRenderer.Bounds(sel.A);
            float px = 1f / (z * SnipScale(sel.Snip)); // one screen pixel in snip units
            b.Inflate(4 * px, 4 * px);
            using var pen = new Pen(Color.FromArgb(0, 120, 215), 1.5f * px) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(pen, b.X, b.Y, b.Width, b.Height);
            g.Restore(s3);
        }
        if (_area is { } area && area.Snip < _snips.Count)
        {
            GraphicsState s4 = g.Save();
            Project.ApplySnipTransform(g, _layout, area.Snip, _snips[area.Snip].Image.Size);
            float px = 1f / (z * SnipScale(area.Snip));
            Rectangle r = area.Rect;
            using (var shade = new SolidBrush(Color.FromArgb(40, 0, 120, 215))) g.FillRectangle(shade, r);
            using (var white = new Pen(Color.White, 1.5f * px)) g.DrawRectangle(white, r.X, r.Y, r.Width, r.Height);
            using (var dash = new Pen(Color.FromArgb(0, 120, 215), 1.5f * px) { DashStyle = DashStyle.Dash }) g.DrawRectangle(dash, r.X, r.Y, r.Width, r.Height);
            g.Restore(s4);
        }
        g.Restore(state);

        if (_overlayEditing && _overlaySel > 0 && _overlaySel < _layout.ImageRects.Length)
        {
            Rectangle sr = ImageRectToClient(_layout.ImageRects[_overlaySel]);
            using var pen = new Pen(Color.FromArgb(0, 120, 215), 2) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(pen, sr);
            foreach (Rectangle h in HandleRects(sr))
            {
                g.FillRectangle(Brushes.White, h);
                g.DrawRectangle(Pens.DodgerBlue, h);
            }
        }
    }

    private Rectangle ImageRectToClient(Rectangle r)
    {
        Point o = Origin();
        float z = Zoom;
        return Rectangle.FromLTRB((int)(o.X + r.Left * z), (int)(o.Y + r.Top * z), (int)(o.X + r.Right * z), (int)(o.Y + r.Bottom * z));
    }

    private int HandleSize => LogicalToDeviceUnits(10);

    /// <summary>Corner handles: top-left, top-right, bottom-left, bottom-right.</summary>
    private Rectangle[] HandleRects(Rectangle r)
    {
        int h = HandleSize, half = h / 2;
        return new[]
        {
            new Rectangle(r.Left - half, r.Top - half, h, h), new Rectangle(r.Right - half, r.Top - half, h, h),
            new Rectangle(r.Left - half, r.Bottom - half, h, h), new Rectangle(r.Right - half, r.Bottom - half, h, h),
        };
    }

    /// <summary>Overlay snip (index ≥ 1) under the mouse, topmost first, and which part of it was hit.</summary>
    private (int Index, OverlayDrag Part) HitOverlay(Point client)
    {
        if (!_overlayEditing || _layout == null || _snips == null) return (-1, OverlayDrag.None);
        if (_overlaySel > 0 && _overlaySel < _layout.ImageRects.Length)
        {
            Rectangle[] hs = HandleRects(ImageRectToClient(_layout.ImageRects[_overlaySel]));
            OverlayDrag[] parts = { OverlayDrag.TopLeft, OverlayDrag.TopRight, OverlayDrag.BottomLeft, OverlayDrag.BottomRight };
            for (int k = 0; k < 4; k++)
                if (Rectangle.Inflate(hs[k], 3, 3).Contains(client)) return (_overlaySel, parts[k]);
        }
        for (int i = _layout.ImageRects.Length - 1; i >= 1; i--)
            if (ImageRectToClient(_layout.ImageRects[i]).Contains(client)) return (i, OverlayDrag.Move);
        return (-1, OverlayDrag.None);
    }

    private RectangleF CurrentOverlayRect(int i)
    {
        Rectangle bg = _layout!.ImageRects[0], r = _layout.ImageRects[i];
        return _snips![i].OverlayRect ?? new RectangleF(r.X - bg.X, r.Y - bg.Y, r.Width, r.Height);
    }

    private void DragOverlay(PointF ip)
    {
        if (_project == null || _snips == null || _overlaySel < 1) return;
        float dx = ip.X - _overlayStartMouse.X, dy = ip.Y - _overlayStartMouse.Y;
        if (!_overlayCheckpointed)
        {
            if (Math.Abs(dx) + Math.Abs(dy) < 1) return;
            _project.Checkpoint();
            _overlayCheckpointed = true;
        }
        RectangleF r = _overlayStartRect;
        if (_overlayDrag == OverlayDrag.Move)
        {
            r.Offset(dx, dy);
        }
        else
        {
            // Corner drag; keeps the proportions unless Shift is held.
            float left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom;
            if (_overlayDrag is OverlayDrag.TopLeft or OverlayDrag.BottomLeft) left += dx; else right += dx;
            if (_overlayDrag is OverlayDrag.TopLeft or OverlayDrag.TopRight) top += dy; else bottom += dy;
            float w = Math.Max(8, right - left), h = Math.Max(8, bottom - top);
            if ((ModifierKeys & Keys.Shift) == 0)
            {
                float aspect = _overlayStartRect.Width / _overlayStartRect.Height;
                if (w / h > aspect) h = w / aspect; else w = h * aspect;
            }
            float x = _overlayDrag is OverlayDrag.TopLeft or OverlayDrag.BottomLeft ? r.Right - w : r.Left;
            float y = _overlayDrag is OverlayDrag.TopLeft or OverlayDrag.TopRight ? r.Bottom - h : r.Top;
            r = new RectangleF(x, y, w, h);
        }
        _snips[_overlaySel].OverlayRect = r;
        OverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void DrawChecker(Graphics g, Rectangle r)
    {
        if (r.IsEmpty) return;
        using var light = new SolidBrush(Color.FromArgb(250, 250, 250));
        using var dark = new SolidBrush(Color.FromArgb(220, 220, 220));
        g.FillRectangle(light, r);
        const int s = 8;
        for (int y = r.Top - r.Top % s; y < r.Bottom; y += s)
            for (int x = r.Left - r.Left % s; x < r.Right; x += s)
                if (((x / s) + (y / s)) % 2 == 0)
                    g.FillRectangle(dark, Rectangle.Intersect(r, new Rectangle(x, y, s, s)));
    }

    // ------------------------------------------------------------------ mouse

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Middle)
        {
            _panning = true;
            _panStart = e.Location;
            _panScrollStart = new Point(-AutoScrollPosition.X, -AutoScrollPosition.Y);
            Cursor = Cursors.SizeAll;
            return;
        }
        if (e.Button != MouseButtons.Left || _project == null || _layout == null || _snips == null || _snips.Count == 0) return;
        CommitText();

        PointF ip = ClientToImage(e.Location);
        int snip = SnipAt(ip);
        switch (_tool)
        {
            case DrawTool.Select:
            {
                // Handles of the selected overlay snip win over drawings under them.
                var overlayHit = HitOverlay(e.Location);
                var hit = overlayHit.Part is OverlayDrag.Move or OverlayDrag.None ? HitAnnotation(ip) : null;
                Select(hit);
                if (hit is { } h)
                {
                    if (e.Clicks >= 2 && h.A.HasText)
                    {
                        BeginTextEdit(h.Snip, h.A, isNew: false);
                        return;
                    }
                    _moving = true;
                    _moveCheckpointed = false;
                    _moveLast = ImageToSnip(ip, h.Snip);
                }
                else if (overlayHit.Index > 0)
                {
                    _overlaySel = overlayHit.Index;
                    _overlayDrag = overlayHit.Part;
                    _overlayCheckpointed = false;
                    _overlayStartMouse = ip;
                    _overlayStartRect = CurrentOverlayRect(overlayHit.Index);
                    SnipClicked?.Invoke(this, overlayHit.Index);
                    Invalidate();
                }
                else
                {
                    _overlaySel = -1;
                    if (snip >= 0) SnipClicked?.Invoke(this, snip);
                    Invalidate();
                }
                break;
            }
            case DrawTool.Area:
            {
                _areaDragging = true;
                _areaStart = ClampToSnip(ImageToSnip(ip, snip), snip);
                _area = (snip, new Rectangle(Point.Round(_areaStart), Size.Empty));
                Invalidate();
                break;
            }
            case DrawTool.Eraser:
                _erasing = true;
                _eraseCheckpointed = false;
                EraseAt(ip);
                break;
            case DrawTool.Pen:
            case DrawTool.Marker:
            case DrawTool.Line:
            case DrawTool.Arrow:
            case DrawTool.Rectangle:
            case DrawTool.Ellipse:
            {
                PointF sp = ImageToSnip(ip, snip);
                _draftSnip = snip;
                _draft = new Annotation
                {
                    Kind = _tool switch
                    {
                        DrawTool.Pen => AnnotationKind.Pen,
                        DrawTool.Marker => AnnotationKind.Marker,
                        DrawTool.Line => AnnotationKind.Line,
                        DrawTool.Arrow => AnnotationKind.Arrow,
                        DrawTool.Rectangle => AnnotationKind.Rectangle,
                        _ => AnnotationKind.Ellipse,
                    },
                    Argb = DrawColor.ToArgb(),
                    Width = LineWidth / SnipScale(snip),
                    Filled = Filled,
                    Points = { sp },
                };
                if (_tool is not (DrawTool.Pen or DrawTool.Marker)) _draft.Points.Add(sp);
                Invalidate();
                break;
            }
            case DrawTool.Text:
            {
                var hit = HitAnnotation(ip, a => a.Kind == AnnotationKind.Text);
                if (hit is { } h)
                {
                    BeginTextEdit(h.Snip, h.A, isNew: false);
                }
                else
                {
                    var a = new Annotation
                    {
                        Kind = AnnotationKind.Text,
                        Argb = DrawColor.ToArgb(),
                        FontSize = FontSizePx / SnipScale(snip),
                        Filled = Filled,
                        Bold = BoldText,
                        Points = { ImageToSnip(ip, snip) },
                    };
                    BeginTextEdit(snip, a, isNew: true);
                }
                break;
            }
            case DrawTool.BadgeCircle:
            case DrawTool.BadgeSquare:
            {
                var hit = HitAnnotation(ip, a => a.IsBadge);
                if (hit is { } h && e.Clicks >= 2)
                {
                    BeginTextEdit(h.Snip, h.A, isNew: false);
                    break;
                }
                _project.Checkpoint();
                var badge = new Annotation
                {
                    Kind = _tool == DrawTool.BadgeCircle ? AnnotationKind.BadgeCircle : AnnotationKind.BadgeSquare,
                    Argb = DrawColor.ToArgb(),
                    FontSize = FontSizePx / SnipScale(snip),
                    Text = _project.NextBadgeNumber.ToString(),
                    Points = { ImageToSnip(ip, snip) },
                };
                _project.NextBadgeNumber++;
                _snips[snip].Annotations.Add(badge);
                RaiseChanged(snip);
                break;
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_panning)
        {
            AutoScrollPosition = new Point(_panScrollStart.X - (e.X - _panStart.X), _panScrollStart.Y - (e.Y - _panStart.Y));
            Invalidate();
            return;
        }
        if (_project == null || _layout == null || _snips == null) return;
        PointF ip = ClientToImage(e.Location);

        if (_overlayDrag != OverlayDrag.None)
        {
            DragOverlay(ip);
        }
        else if (_areaDragging && _area is { } a)
        {
            PointF p2 = ClampToSnip(ImageToSnip(ip, a.Snip), a.Snip);
            _area = (a.Snip, Rectangle.FromLTRB(
                (int)Math.Floor(Math.Min(_areaStart.X, p2.X)), (int)Math.Floor(Math.Min(_areaStart.Y, p2.Y)),
                (int)Math.Ceiling(Math.Max(_areaStart.X, p2.X)), (int)Math.Ceiling(Math.Max(_areaStart.Y, p2.Y))));
            Invalidate();
        }
        else if (_draft != null)
        {
            PointF sp = ImageToSnip(ip, _draftSnip);
            if (_draft.Kind is AnnotationKind.Pen or AnnotationKind.Marker)
            {
                PointF last = _draft.Points[^1];
                float minStep = 1f / (Zoom * SnipScale(_draftSnip));
                if (Math.Abs(sp.X - last.X) + Math.Abs(sp.Y - last.Y) >= minStep) _draft.Points.Add(sp);
            }
            else
            {
                _draft.Points[1] = (ModifierKeys & Keys.Shift) != 0 ? Constrain(_draft.Kind, _draft.Points[0], sp) : sp;
            }
            Invalidate();
        }
        else if (_moving && _selected is { } s)
        {
            PointF sp = ImageToSnip(ip, s.Snip);
            float dx = sp.X - _moveLast.X, dy = sp.Y - _moveLast.Y;
            if (dx == 0 && dy == 0) return;
            if (!_moveCheckpointed)
            {
                _project.Checkpoint();
                _moveCheckpointed = true;
            }
            s.A.Offset(dx, dy);
            _moveLast = sp;
            Invalidate();
        }
        else if (_erasing)
        {
            EraseAt(ip);
        }
        else if (_tool == DrawTool.Select && e.Button == MouseButtons.None)
        {
            var oh = HitOverlay(e.Location);
            Cursor = oh.Part switch
            {
                OverlayDrag.TopLeft or OverlayDrag.BottomRight => Cursors.SizeNWSE,
                OverlayDrag.TopRight or OverlayDrag.BottomLeft => Cursors.SizeNESW,
                _ when HitAnnotation(ip) != null || oh.Index > 0 => Cursors.SizeAll,
                _ => Cursors.Default,
            };
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_panning)
        {
            _panning = false;
            Tool = _tool; // restore cursor
            return;
        }
        if (_project == null) return;

        if (_overlayDrag != OverlayDrag.None)
        {
            _overlayDrag = OverlayDrag.None;
            if (_overlayCheckpointed) DocumentChanged?.Invoke(this, EventArgs.Empty);
        }
        if (_areaDragging)
        {
            _areaDragging = false;
            if (_area is { } a && (a.Rect.Width < 2 || a.Rect.Height < 2)) _area = null;
            AreaChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
        if (_draft != null)
        {
            Annotation d = _draft;
            int snip = _draftSnip;
            _draft = null;
            bool keep = d.Kind is AnnotationKind.Pen or AnnotationKind.Marker || Length(d.Points[0], d.Points[1]) * Zoom * SnipScale(snip) >= 3;
            if (keep)
            {
                _project.Checkpoint();
                _snips![snip].Annotations.Add(d);
                RaiseChanged(snip);
            }
            Invalidate();
        }
        if (_moving)
        {
            _moving = false;
            if (_moveCheckpointed && _selected is { } s) RaiseChanged(s.Snip);
        }
        if (_erasing)
        {
            _erasing = false;
            if (_eraseCheckpointed) RaiseChanged(-1);
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Control) != 0)
        {
            ZoomBy(e.Delta > 0 ? 1.25f : 0.8f, e.Location);
            return;
        }
        if ((ModifierKeys & Keys.Shift) != 0)
        {
            AutoScrollPosition = new Point(-AutoScrollPosition.X - e.Delta, -AutoScrollPosition.Y);
            Invalidate();
            return;
        }
        base.OnMouseWheel(e);
        Invalidate();
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        Invalidate();
    }

    private void EraseAt(PointF ip)
    {
        var hit = HitAnnotation(ip);
        if (hit is not { } h || _project == null) return;
        if (!_eraseCheckpointed)
        {
            _project.Checkpoint();
            _eraseCheckpointed = true;
        }
        _snips![h.Snip].Annotations.Remove(h.A);
        if (_selected?.A == h.A) Select(null);
        _snips[h.Snip].InvalidateThumbnail();
        Invalidate();
    }

    private PointF ClampToSnip(PointF sp, int i)
    {
        Size s = _snips![i].Image.Size;
        return new PointF(Math.Clamp(sp.X, 0, s.Width), Math.Clamp(sp.Y, 0, s.Height));
    }

    /// <summary>Shift while drawing: square/circle, or lines at 45° steps.</summary>
    private static PointF Constrain(AnnotationKind kind, PointF a, PointF b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        if (kind is AnnotationKind.Rectangle or AnnotationKind.Ellipse)
        {
            float s = Math.Max(Math.Abs(dx), Math.Abs(dy));
            return new PointF(a.X + Math.Sign(dx == 0 ? 1 : dx) * s, a.Y + Math.Sign(dy == 0 ? 1 : dy) * s);
        }
        double angle = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
        double len = Math.Sqrt(dx * dx + dy * dy);
        return new PointF(a.X + (float)(Math.Cos(angle) * len), a.Y + (float)(Math.Sin(angle) * len));
    }

    private static float Length(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    // ------------------------------------------------------------------ keyboard

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & ~Keys.Shift) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_selected is not { } s || _project == null) return;
        float step = (e.Shift ? 10f : 1f) / (Zoom * SnipScale(s.Snip));
        (float dx, float dy) = e.KeyCode switch
        {
            Keys.Left => (-step, 0f),
            Keys.Right => (step, 0f),
            Keys.Up => (0f, -step),
            Keys.Down => (0f, step),
            _ => (0f, 0f),
        };
        if (dx == 0 && dy == 0) return;
        _project.Checkpoint();
        s.A.Offset(dx, dy);
        RaiseChanged(s.Snip);
        e.Handled = true;
    }

    // ------------------------------------------------------------------ text editing

    private void BeginTextEdit(int snip, Annotation a, bool isNew)
    {
        _editing = (snip, a, isNew);
        Select(isNew ? null : (snip, a));
        float px = a.FontSize * SnipScale(snip) * Zoom;
        _editor.Font = new Font("Segoe UI", Math.Max(6f, px), a.Bold || a.IsBadge ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        _editor.ForeColor = a.IsBadge ? Color.Black : a.Color;
        _editor.Text = a.Text;
        Point at = SnipToClient(a.Points[0], snip);
        if (a.IsBadge) at.Offset(-(int)(px * 1.2f), -(int)(px * 0.8f));
        _editor.Location = at;
        SizeEditor();
        _editor.Visible = true;
        _editor.Focus();
        _editor.SelectAll();
    }

    private void SizeEditor()
    {
        Size s = TextRenderer.MeasureText(_editor.Text.Length == 0 ? "Type here" : _editor.Text + "W", _editor.Font);
        _editor.Size = new Size(Math.Max(60, s.Width + 12), s.Height + 8);
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            CancelText();
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Enter && !e.Shift)
        {
            CommitText();
            e.SuppressKeyPress = true;
        }
    }

    private void CommitText(bool refocus = true)
    {
        if (_editing is not { } ed || _project == null) return;
        _editing = null;
        _editor.Visible = false;
        string text = _editor.Text.TrimEnd();
        if (ed.IsNew)
        {
            if (text.Length > 0)
            {
                _project.Checkpoint();
                ed.A.Text = text;
                _snips![ed.Snip].Annotations.Add(ed.A);
                RaiseChanged(ed.Snip);
            }
        }
        else if (text != ed.A.Text)
        {
            _project.Checkpoint();
            if (text.Length == 0) _snips![ed.Snip].Annotations.Remove(ed.A);
            else ed.A.Text = text;
            RaiseChanged(ed.Snip);
        }
        if (refocus) Focus();
        Invalidate();
    }

    private void CancelText()
    {
        if (_editing == null) return;
        _editing = null;
        _editor.Visible = false;
        Focus();
    }

    // ------------------------------------------------------------------ helpers

    private void Select((int Snip, Annotation A)? sel)
    {
        bool changed = !Equals(sel?.A, _selected?.A);
        _selected = sel;
        if (changed) SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void RaiseChanged(int snip)
    {
        if (_project != null)
        {
            if (_snips != null && snip >= 0 && snip < _snips.Count) _snips[snip].InvalidateThumbnail();
            else if (_snips != null) foreach (SnipItem s in _snips) s.InvalidateThumbnail();
        }
        Invalidate();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) InvalidateScaledCache();
        base.Dispose(disposing);
    }
}
