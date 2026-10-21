namespace MkMermaid.Scene;

/// <summary>
/// A laid-out diagram in pixels, y pointing down, origin at the top-left corner.
/// Items are drawn in order. Colors are <c>#RRGGBB</c> or <c>#RRGGBBAA</c>; null means "not painted".
/// </summary>
public sealed record DiagramScene(
    DiagramKind Kind,
    double Width,
    double Height,
    string? Background,
    IReadOnlyList<SceneItem> Items,
    string? Title,
    string? Description,
    MermaidError? Error,
    string FontFamily);

public abstract record SceneItem;

/// <summary>A filled and/or stroked path. When <see cref="StrokeGradient"/> is set it paints the stroke instead of <see cref="Stroke"/>.</summary>
public sealed record ShapeItem(
    PathGeometry Geometry,
    string? Fill,
    string? Stroke,
    double StrokeWidth,
    IReadOnlyList<double>? Dash = null,
    double Opacity = 1,
    LinearGradient? StrokeGradient = null) : SceneItem;

/// <summary>Two-stop gradient along the line from (X1, Y1) to (X2, Y2), in scene coordinates.</summary>
public sealed record LinearGradient(double X1, double Y1, double X2, double Y2, string From, string To);

/// <summary>One line of text. <see cref="Y"/> is the alphabetic baseline; <see cref="Rotation"/> is in degrees around (X, Y).</summary>
public sealed record TextItem(
    double X,
    double Y,
    IReadOnlyList<TextRun> Runs,
    double FontSize,
    TextAnchor Anchor,
    string Color,
    double Rotation = 0) : SceneItem
{
    public string Text => string.Concat(Runs.Select(r => r.Text));
}

public sealed record TextRun(string Text, bool Bold = false, bool Italic = false, bool Underline = false, bool Strike = false);

public enum TextAnchor
{
    Start,
    Middle,
    End
}

public enum PathCommandKind
{
    MoveTo,
    LineTo,
    CubicTo,
    Close
}

/// <summary>For <see cref="PathCommandKind.CubicTo"/>, (X1, Y1) and (X2, Y2) are the control points; (X, Y) is the end point.</summary>
public readonly record struct PathCommand(PathCommandKind Kind, double X, double Y, double X1 = 0, double Y1 = 0, double X2 = 0, double Y2 = 0);

public sealed class PathGeometry
{
    private readonly List<PathCommand> _commands = [];

    public IReadOnlyList<PathCommand> Commands => _commands;

    public bool IsEmpty => _commands.Count == 0;

    public PathGeometry MoveTo(double x, double y)
    {
        _commands.Add(new PathCommand(PathCommandKind.MoveTo, x, y));
        return this;
    }

    public PathGeometry LineTo(double x, double y)
    {
        _commands.Add(new PathCommand(PathCommandKind.LineTo, x, y));
        return this;
    }

    public PathGeometry CubicTo(double x1, double y1, double x2, double y2, double x, double y)
    {
        _commands.Add(new PathCommand(PathCommandKind.CubicTo, x, y, x1, y1, x2, y2));
        return this;
    }

    public PathGeometry Close()
    {
        _commands.Add(new PathCommand(PathCommandKind.Close, 0, 0));
        return this;
    }

    /// <summary>Elliptical arc from the current point, approximated with cubic segments of at most 90 degrees.</summary>
    public PathGeometry ArcTo(double cx, double cy, double rx, double ry, double startRadians, double sweepRadians)
    {
        var segments = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweepRadians) / (Math.PI / 2) - 1e-9));
        var step = sweepRadians / segments;
        var k = 4.0 / 3.0 * Math.Tan(step / 4);
        var angle = startRadians;
        for (var i = 0; i < segments; i++)
        {
            var (c0, s0) = (Math.Cos(angle), Math.Sin(angle));
            var (c1, s1) = (Math.Cos(angle + step), Math.Sin(angle + step));
            CubicTo(
                cx + rx * (c0 - k * s0), cy + ry * (s0 + k * c0),
                cx + rx * (c1 + k * s1), cy + ry * (s1 - k * c1),
                cx + rx * c1, cy + ry * s1);
            angle += step;
        }

        return this;
    }

    public static PathGeometry Rectangle(double x, double y, double width, double height, double radius = 0)
    {
        var path = new PathGeometry();
        var r = Math.Min(radius, Math.Min(width, height) / 2);
        if (r <= 0)
        {
            return path.MoveTo(x, y).LineTo(x + width, y).LineTo(x + width, y + height).LineTo(x, y + height).Close();
        }

        path.MoveTo(x + r, y).LineTo(x + width - r, y);
        path.ArcTo(x + width - r, y + r, r, r, -Math.PI / 2, Math.PI / 2);
        path.LineTo(x + width, y + height - r);
        path.ArcTo(x + width - r, y + height - r, r, r, 0, Math.PI / 2);
        path.LineTo(x + r, y + height);
        path.ArcTo(x + r, y + height - r, r, r, Math.PI / 2, Math.PI / 2);
        path.LineTo(x, y + r);
        path.ArcTo(x + r, y + r, r, r, Math.PI, Math.PI / 2);
        return path.Close();
    }

    public static PathGeometry Ellipse(double cx, double cy, double rx, double ry)
    {
        var path = new PathGeometry().MoveTo(cx + rx, cy);
        path.ArcTo(cx, cy, rx, ry, 0, 2 * Math.PI);
        return path.Close();
    }

    public static PathGeometry Polygon(IEnumerable<(double X, double Y)> points)
    {
        var path = new PathGeometry();
        var first = true;
        foreach (var (x, y) in points)
        {
            if (first)
            {
                path.MoveTo(x, y);
                first = false;
            }
            else
            {
                path.LineTo(x, y);
            }
        }

        return first ? path : path.Close();
    }

    public static PathGeometry Polyline(IEnumerable<(double X, double Y)> points)
    {
        var path = new PathGeometry();
        var first = true;
        foreach (var (x, y) in points)
        {
            if (first)
            {
                path.MoveTo(x, y);
                first = false;
            }
            else
            {
                path.LineTo(x, y);
            }
        }

        return path;
    }

    public static PathGeometry Line(double x1, double y1, double x2, double y2) => new PathGeometry().MoveTo(x1, y1).LineTo(x2, y2);

    internal PathGeometry Translate(double dx, double dy)
    {
        var result = new PathGeometry();
        foreach (var c in _commands)
        {
            result._commands.Add(c.Kind == PathCommandKind.Close
                ? c
                : c with { X = c.X + dx, Y = c.Y + dy, X1 = c.X1 + dx, Y1 = c.Y1 + dy, X2 = c.X2 + dx, Y2 = c.Y2 + dy });
        }

        return result;
    }

    internal void Extend(ref Bounds bounds)
    {
        foreach (var c in _commands)
        {
            switch (c.Kind)
            {
                case PathCommandKind.MoveTo or PathCommandKind.LineTo:
                    bounds.Add(c.X, c.Y);
                    break;
                case PathCommandKind.CubicTo:
                    bounds.Add(c.X, c.Y);
                    bounds.Add(c.X1, c.Y1);
                    bounds.Add(c.X2, c.Y2);
                    break;
            }
        }
    }
}

internal struct Bounds
{
    public double MinX = double.PositiveInfinity;
    public double MinY = double.PositiveInfinity;
    public double MaxX = double.NegativeInfinity;
    public double MaxY = double.NegativeInfinity;

    public Bounds()
    {
    }

    public readonly bool IsEmpty => MinX > MaxX;

    public void Add(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            return;
        }

        MinX = Math.Min(MinX, x);
        MinY = Math.Min(MinY, y);
        MaxX = Math.Max(MaxX, x);
        MaxY = Math.Max(MaxY, y);
    }
}
