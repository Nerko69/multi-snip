using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace MultiSnip;

/// <summary>Draws annotations and finds which one is under the mouse. All coordinates are snip pixels.</summary>
public static class AnnotationRenderer
{
    private static readonly Graphics Measure = Graphics.FromImage(new Bitmap(1, 1));

    public static void DrawAll(Graphics g, IEnumerable<Annotation> annotations)
    {
        foreach (Annotation a in annotations) Draw(g, a);
    }

    public static void Draw(Graphics g, Annotation a)
    {
        SmoothingMode oldSmoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        try
        {
            switch (a.Kind)
            {
                case AnnotationKind.Pen:
                case AnnotationKind.Marker:
                    DrawStroke(g, a);
                    break;
                case AnnotationKind.Line:
                case AnnotationKind.Arrow:
                    DrawLine(g, a);
                    break;
                case AnnotationKind.Rectangle:
                case AnnotationKind.Ellipse:
                    DrawShape(g, a);
                    break;
                case AnnotationKind.Text:
                    DrawText(g, a);
                    break;
                case AnnotationKind.BadgeCircle:
                case AnnotationKind.BadgeSquare:
                    DrawBadge(g, a);
                    break;
            }
        }
        finally
        {
            g.SmoothingMode = oldSmoothing;
        }
    }

    // ------------------------------------------------------------------ drawing

    private static Color StrokeColor(Annotation a) =>
        a.Kind == AnnotationKind.Marker && a.Color.A == 255 ? Color.FromArgb(110, a.Color) : a.Color;

    private static Pen MakePen(Annotation a)
    {
        var pen = new Pen(StrokeColor(a), Math.Max(0.5f, a.Width)) { LineJoin = LineJoin.Round };
        if (a.Kind == AnnotationKind.Marker)
        {
            pen.StartCap = pen.EndCap = LineCap.Square; // highlighter look
        }
        else
        {
            pen.StartCap = pen.EndCap = LineCap.Round;
        }
        return pen;
    }

    private static void DrawStroke(Graphics g, Annotation a)
    {
        if (a.Points.Count == 0) return;
        if (a.Points.Count == 1)
        {
            float r = a.Width / 2;
            using var b = new SolidBrush(StrokeColor(a));
            g.FillEllipse(b, a.Points[0].X - r, a.Points[0].Y - r, a.Width, a.Width);
            return;
        }
        using Pen pen = MakePen(a);
        // One path, so a translucent marker doesn't get darker where segments overlap.
        using var path = new GraphicsPath();
        path.AddLines(a.Points.ToArray());
        g.DrawPath(pen, path);
    }

    private static void DrawLine(Graphics g, Annotation a)
    {
        if (a.Points.Count < 2) return;
        PointF p1 = a.Points[0], p2 = a.Points[^1];
        using Pen pen = MakePen(a);
        if (a.Kind == AnnotationKind.Arrow && ArrowHead(a, out PointF[] head, out PointF lineEnd))
        {
            g.DrawLine(pen, p1, lineEnd);
            using var b = new SolidBrush(a.Color);
            g.FillPolygon(b, head);
        }
        else
        {
            g.DrawLine(pen, p1, p2);
        }
    }

    /// <summary>Arrow head triangle at the end point, sized by the line width.</summary>
    private static bool ArrowHead(Annotation a, out PointF[] head, out PointF lineEnd)
    {
        PointF p1 = a.Points[0], p2 = a.Points[^1];
        float dx = p2.X - p1.X, dy = p2.Y - p1.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        head = Array.Empty<PointF>();
        lineEnd = p2;
        if (len < 0.5f) return false;
        float ux = dx / len, uy = dy / len;
        float headLen = Math.Min(len, Math.Max(10f, a.Width * 4f));
        float halfW = headLen * 0.55f;
        var basePt = new PointF(p2.X - ux * headLen, p2.Y - uy * headLen);
        head = new[]
        {
            p2,
            new PointF(basePt.X - uy * halfW, basePt.Y + ux * halfW),
            new PointF(basePt.X + uy * halfW, basePt.Y - ux * halfW),
        };
        lineEnd = new PointF(p2.X - ux * headLen * 0.8f, p2.Y - uy * headLen * 0.8f);
        return true;
    }

    private static RectangleF ShapeRect(Annotation a)
    {
        if (a.Points.Count < 2) return RectangleF.Empty;
        PointF p1 = a.Points[0], p2 = a.Points[^1];
        return RectangleF.FromLTRB(Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y), Math.Max(p1.X, p2.X), Math.Max(p1.Y, p2.Y));
    }

    private static void DrawShape(Graphics g, Annotation a)
    {
        RectangleF r = ShapeRect(a);
        if (r.Width <= 0 && r.Height <= 0) return;
        if (a.Filled)
        {
            using var b = new SolidBrush(a.Color);
            if (a.Kind == AnnotationKind.Ellipse) g.FillEllipse(b, r);
            else g.FillRectangle(b, r);
        }
        else
        {
            using Pen pen = MakePen(a);
            pen.LineJoin = LineJoin.Miter;
            if (a.Kind == AnnotationKind.Ellipse) g.DrawEllipse(pen, r);
            else g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
        }
    }

    private static Font TextFont(Annotation a) =>
        new("Segoe UI", Math.Max(4f, a.FontSize), a.Bold || a.IsBadge ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);

    private static readonly StringFormat Typographic = new(StringFormat.GenericTypographic)
    {
        FormatFlags = StringFormatFlags.MeasureTrailingSpaces,
    };

    /// <summary>Text box of a Text annotation (anchor = top-left).</summary>
    public static RectangleF TextRect(Annotation a)
    {
        using Font f = TextFont(a);
        string text = a.Text.Length == 0 ? " " : a.Text;
        SizeF size = Measure.MeasureString(text, f, PointF.Empty, Typographic);
        float pad = a.Filled ? a.FontSize * 0.25f : 0;
        PointF p = a.Points.Count > 0 ? a.Points[0] : PointF.Empty;
        return new RectangleF(p.X - pad, p.Y - pad, size.Width + 2 * pad, size.Height + 2 * pad);
    }

    private static void DrawText(Graphics g, Annotation a)
    {
        if (a.Points.Count == 0 || a.Text.Length == 0) return;
        using Font f = TextFont(a);
        if (a.Filled)
        {
            RectangleF box = TextRect(a);
            using var bg = new SolidBrush(Color.FromArgb(235, 255, 255, 255));
            using GraphicsPath rounded = RoundedRect(box, a.FontSize * 0.2f);
            g.FillPath(bg, rounded);
        }
        using var brush = new SolidBrush(a.Color);
        g.DrawString(a.Text, f, brush, a.Points[0], Typographic);
    }

    /// <summary>Outer rectangle of a badge (anchor = centre).</summary>
    public static RectangleF BadgeRect(Annotation a)
    {
        using Font f = TextFont(a);
        SizeF text = Measure.MeasureString(a.Text.Length == 0 ? "0" : a.Text, f, PointF.Empty, Typographic);
        float h = a.FontSize * 1.8f;
        float w = Math.Max(h, text.Width + a.FontSize * 1.0f);
        PointF c = a.Points.Count > 0 ? a.Points[0] : PointF.Empty;
        return new RectangleF(c.X - w / 2, c.Y - h / 2, w, h);
    }

    private static void DrawBadge(Graphics g, Annotation a)
    {
        if (a.Points.Count == 0) return;
        RectangleF r = BadgeRect(a);
        float ring = Math.Max(1.5f, a.FontSize * 0.12f);
        using GraphicsPath shape = a.Kind == AnnotationKind.BadgeCircle
            ? Capsule(r)
            : RoundedRect(r, r.Height * 0.22f);
        using (var fill = new SolidBrush(a.Color)) g.FillPath(fill, shape);
        using (var pen = new Pen(Color.White, ring)) g.DrawPath(pen, shape);

        using Font f = TextFont(a);
        Color textColor = Luminance(a.Color) > 0.62 ? Color.Black : Color.White;
        using var tb = new SolidBrush(textColor);
        using var centred = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        g.DrawString(a.Text, f, tb, new RectangleF(r.X, r.Y + a.FontSize * 0.04f, r.Width, r.Height), centred);
    }

    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;

    private static GraphicsPath Capsule(RectangleF r)
    {
        var p = new GraphicsPath();
        if (r.Width <= r.Height + 0.5f)
        {
            p.AddEllipse(r);
            return p;
        }
        float d = r.Height;
        p.AddArc(r.X, r.Y, d, d, 90, 180);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
        p.CloseFigure();
        return p;
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f)
        {
            p.AddRectangle(r);
            return p;
        }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // ------------------------------------------------------------------ geometry

    /// <summary>Bounding box including the line width.</summary>
    public static RectangleF Bounds(Annotation a)
    {
        switch (a.Kind)
        {
            case AnnotationKind.Text:
                return TextRect(a);
            case AnnotationKind.BadgeCircle:
            case AnnotationKind.BadgeSquare:
                return BadgeRect(a);
        }
        if (a.Points.Count == 0) return RectangleF.Empty;
        float minX = a.Points.Min(p => p.X), minY = a.Points.Min(p => p.Y);
        float maxX = a.Points.Max(p => p.X), maxY = a.Points.Max(p => p.Y);
        float pad = a.Kind == AnnotationKind.Arrow ? Math.Max(10f, a.Width * 4f) : a.Width / 2;
        return RectangleF.FromLTRB(minX - pad, minY - pad, maxX + pad, maxY + pad);
    }

    /// <summary>True if <paramref name="p"/> is on the annotation (within <paramref name="tolerance"/> snip pixels).</summary>
    public static bool HitTest(Annotation a, PointF p, float tolerance)
    {
        RectangleF bounds = Bounds(a);
        bounds.Inflate(tolerance, tolerance);
        if (!bounds.Contains(p)) return false;

        switch (a.Kind)
        {
            case AnnotationKind.Text:
            case AnnotationKind.BadgeCircle:
            case AnnotationKind.BadgeSquare:
                return true;
            case AnnotationKind.Rectangle:
            case AnnotationKind.Ellipse:
            {
                RectangleF r = ShapeRect(a);
                using var path = new GraphicsPath();
                if (a.Kind == AnnotationKind.Ellipse) path.AddEllipse(r); else path.AddRectangle(r);
                if (a.Filled && path.IsVisible(p)) return true;
                using var pen = new Pen(Color.Black, a.Width + 2 * tolerance);
                return path.IsOutlineVisible(p, pen, Measure);
            }
            default:
            {
                if (a.Points.Count == 1)
                    return Distance(p, a.Points[0]) <= a.Width / 2 + tolerance;
                using var path = new GraphicsPath();
                path.AddLines(a.Kind is AnnotationKind.Line or AnnotationKind.Arrow
                    ? new[] { a.Points[0], a.Points[^1] }
                    : a.Points.ToArray());
                using var pen = new Pen(Color.Black, a.Width + 2 * tolerance) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                return path.IsOutlineVisible(p, pen, Measure);
            }
        }
    }

    private static float Distance(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
