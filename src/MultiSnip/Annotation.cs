using System.Text.Json.Serialization;

namespace MultiSnip;

public enum AnnotationKind { Pen, Marker, Line, Arrow, Rectangle, Ellipse, Text, BadgeCircle, BadgeSquare }

/// <summary>
/// Something drawn on a snip (pen stroke, shape, text, numbered badge). Coordinates are in the
/// snip's own pixels, so annotations stay attached to their snip when the layout changes, and are
/// saved with the project so they stay editable.
/// </summary>
public sealed class Annotation
{
    public AnnotationKind Kind { get; set; }

    /// <summary>Stroke points (pen/marker), or two corner/end points (shapes), or the anchor (text/badges).</summary>
    public List<PointF> Points { get; set; } = new();

    public int Argb { get; set; } = unchecked((int)0xFFE81123);

    /// <summary>Line width (pen, marker, shapes) in snip pixels.</summary>
    public float Width { get; set; } = 3;

    /// <summary>Shapes: filled instead of outlined. Text: drawn on a white box.</summary>
    public bool Filled { get; set; }

    /// <summary>Text and badges: font size in snip pixels.</summary>
    public float FontSize { get; set; } = 20;

    /// <summary>Text: bold font.</summary>
    public bool Bold { get; set; } = true;

    /// <summary>Text, or the badge label (usually its number).</summary>
    public string Text { get; set; } = "";

    [JsonIgnore]
    public Color Color => Color.FromArgb(Argb);

    [JsonIgnore]
    public bool IsBadge => Kind is AnnotationKind.BadgeCircle or AnnotationKind.BadgeSquare;

    [JsonIgnore]
    public bool HasText => Kind == AnnotationKind.Text || IsBadge;

    public Annotation Clone()
    {
        var a = (Annotation)MemberwiseClone();
        a.Points = new List<PointF>(Points);
        return a;
    }

    public void Offset(float dx, float dy)
    {
        for (int i = 0; i < Points.Count; i++)
            Points[i] = new PointF(Points[i].X + dx, Points[i].Y + dy);
    }

    /// <summary>Numeric badge label, if any (used to suggest the next number).</summary>
    [JsonIgnore]
    public int? BadgeNumber => IsBadge && int.TryParse(Text, out int n) ? n : null;
}
