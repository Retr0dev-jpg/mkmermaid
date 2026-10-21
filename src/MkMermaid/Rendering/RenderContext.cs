using MkMermaid.Config;
using MkMermaid.Scene;
using MkMermaid.Text;
using MkMermaid.Theming;

namespace MkMermaid.Rendering;

internal sealed class RenderContext(Theme theme, MermaidConfig config, ITextMeasurer measurer, double fontSize)
{
    public Theme Theme { get; } = theme;

    public MermaidConfig Config { get; } = config;

    public ITextMeasurer Measurer { get; } = measurer;

    public double FontSize { get; } = fontSize;

    public MeasuredText Measure(RichText text, double? fontSize = null, double maxWidth = double.PositiveInfinity, bool forceWrap = false) =>
        MeasuredText.Measure(text, fontSize ?? FontSize, Measurer, maxWidth, forceWrap);

    public MeasuredText Measure(string? text, double? fontSize = null, double maxWidth = double.PositiveInfinity, bool forceWrap = false) =>
        Measure(RichText.Parse(text), fontSize, maxWidth, forceWrap);

    public MeasuredText MeasurePlain(string text, double? fontSize = null, bool bold = false) =>
        Measure(RichText.Styled(text, bold), fontSize);

    public double TextWidth(string text, double? fontSize = null, bool bold = false) =>
        Measurer.Measure(text, fontSize ?? FontSize, bold, false).Width;

    public Canvas CreateCanvas() => new(this);
}

/// <summary>Collects scene items in arbitrary coordinates; <see cref="Finish"/> moves them to the origin with padding.</summary>
internal sealed class Canvas(RenderContext context)
{
    private readonly List<SceneItem> _items = [];

    public RenderContext Context { get; } = context;

    public int Count => _items.Count;

    public void Add(SceneItem item) => _items.Add(item);

    /// <summary>Adds everything drawn on <paramref name="layer"/> on top of this canvas.</summary>
    public void Append(Canvas layer) => _items.AddRange(layer._items);

    public void Shape(PathGeometry geometry, string? fill, string? stroke, double strokeWidth = 1, IReadOnlyList<double>? dash = null, double opacity = 1)
    {
        if (geometry.IsEmpty || (fill is null && (stroke is null || strokeWidth <= 0)))
        {
            return;
        }

        _items.Add(new ShapeItem(geometry, fill, stroke, strokeWidth, dash, opacity));
    }

    public void GradientStroke(PathGeometry geometry, LinearGradient gradient, double strokeWidth, double opacity = 1)
    {
        if (!geometry.IsEmpty && strokeWidth > 0)
        {
            _items.Add(new ShapeItem(geometry, null, gradient.From, strokeWidth, null, opacity, gradient));
        }
    }

    public void Rect(double x, double y, double width, double height, string? fill, string? stroke, double strokeWidth = 1, double radius = 0, IReadOnlyList<double>? dash = null, double opacity = 1) =>
        Shape(PathGeometry.Rectangle(x, y, width, height, radius), fill, stroke, strokeWidth, dash, opacity);

    public void Circle(double cx, double cy, double r, string? fill, string? stroke, double strokeWidth = 1, IReadOnlyList<double>? dash = null) =>
        Shape(PathGeometry.Ellipse(cx, cy, r, r), fill, stroke, strokeWidth, dash);

    public void Line(double x1, double y1, double x2, double y2, string stroke, double strokeWidth = 1, IReadOnlyList<double>? dash = null) =>
        Shape(PathGeometry.Line(x1, y1, x2, y2), null, stroke, strokeWidth, dash);

    /// <summary>Single-style text; <paramref name="y"/> is the baseline.</summary>
    public void Text(string text, double x, double y, double fontSize, TextAnchor anchor, string color, bool bold = false, bool italic = false, double rotation = 0)
    {
        if (text.Length == 0)
        {
            return;
        }

        _items.Add(new TextItem(x, y, [new TextRun(text, bold, italic)], fontSize, anchor, color, rotation));
    }

    /// <summary>Draws a measured label whose first line box starts at <paramref name="top"/>.</summary>
    public void Label(MeasuredText text, double x, double top, TextAnchor anchor, string color, bool bold = false, double rotation = 0)
    {
        var y = top;
        foreach (var (runs, _) in text.Lines)
        {
            var baseline = y + (text.LineHeight - (text.Ascent + text.Descent)) / 2 + text.Ascent;
            var styled = bold ? runs.Select(r => r with { Bold = true }).ToList() : runs;
            if (styled.Any(r => r.Text.Length > 0))
            {
                _items.Add(new TextItem(x, baseline, styled, text.FontSize, anchor, color, rotation));
            }

            y += text.LineHeight;
        }
    }

    public void LabelCentered(MeasuredText text, double cx, double cy, string color, bool bold = false) =>
        Label(text, cx, cy - text.Height / 2, TextAnchor.Middle, color, bold);

    /// <summary>Extent of everything drawn so far (strokes and text included).</summary>
    public Bounds Extent()
    {
        var bounds = new Bounds();
        foreach (var item in _items)
        {
            switch (item)
            {
                case ShapeItem shape:
                    var b = new Bounds();
                    shape.Geometry.Extend(ref b);
                    if (!b.IsEmpty)
                    {
                        var half = shape.Stroke is null ? 0 : shape.StrokeWidth / 2;
                        bounds.Add(b.MinX - half, b.MinY - half);
                        bounds.Add(b.MaxX + half, b.MaxY + half);
                    }

                    break;
                case TextItem text:
                    AddText(ref bounds, text);
                    break;
            }
        }

        if (bounds.IsEmpty)
        {
            bounds.Add(0, 0);
        }

        return bounds;
    }

    /// <summary>Draws a title centered above everything drawn so far.</summary>
    public void Title(string? title, string color, double fontSize = 18)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        var bounds = Extent();
        var text = Context.MeasurePlain(title, fontSize);
        Label(text, (bounds.MinX + bounds.MaxX) / 2, bounds.MinY - text.Height - 8, TextAnchor.Middle, color);
    }

    public DiagramScene Finish(DiagramKind kind, string? title, string? description, double padding, string? background, MermaidError? error = null)
    {
        var bounds = Extent();
        var dx = padding - bounds.MinX;
        var dy = padding - bounds.MinY;
        var moved = _items.Select(item => item switch
        {
            ShapeItem s => (SceneItem)(s with
            {
                Geometry = s.Geometry.Translate(dx, dy),
                Fill = Paint(s.Fill),
                Stroke = Paint(s.Stroke),
                StrokeGradient = s.StrokeGradient is { } g
                    ? g with { X1 = g.X1 + dx, Y1 = g.Y1 + dy, X2 = g.X2 + dx, Y2 = g.Y2 + dy, From = Paint(g.From) ?? "#000000", To = Paint(g.To) ?? "#000000" }
                    : null
            }),
            TextItem t => t with { X = t.X + dx, Y = t.Y + dy, Color = Paint(t.Color) ?? "#000000" },
            _ => item
        }).ToList();
        var width = Math.Ceiling(bounds.MaxX - bounds.MinX + 2 * padding);
        var height = Math.Ceiling(bounds.MaxY - bounds.MinY + 2 * padding);
        return new DiagramScene(kind, width, height, Paint(background), moved, title, description, error, Context.Measurer.FontFamily);
    }

    /// <summary>Scene colors are always #RRGGBB or #RRGGBBAA, whatever a diagram passed in; anything else is unpainted.</summary>
    private static string? Paint(string? color) => color is not null && Theming.Color.TryParse(color, out var c) ? c.ToString() : null;

    private void AddText(ref Bounds bounds, TextItem text)
    {
        var width = MeasuredText.RunsWidth(text.Runs, text.FontSize, Context.Measurer);
        var metrics = Context.Measurer.Measure("Hg", text.FontSize, false, false);
        var left = text.Anchor switch
        {
            TextAnchor.Middle => -width / 2,
            TextAnchor.End => -width,
            _ => 0
        };
        (double X, double Y)[] corners = [(left, -metrics.Ascent), (left + width, -metrics.Ascent), (left, metrics.Descent), (left + width, metrics.Descent)];
        var radians = text.Rotation * Math.PI / 180;
        var (cos, sin) = (Math.Cos(radians), Math.Sin(radians));
        foreach (var (x, y) in corners)
        {
            bounds.Add(text.X + x * cos - y * sin, text.Y + x * sin + y * cos);
        }
    }
}
