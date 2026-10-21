using System.Text.RegularExpressions;
using MkMermaid.Parsing;
using MkMermaid.Rendering;
using MkMermaid.Scene;
using MkMermaid.Theming;
using static MkMermaid.Parsing.ParseHelpers;

namespace MkMermaid.Diagrams.Quadrant;

internal sealed record QuadrantPoint(string Label, double X, double Y, string? ClassName, QuadrantPointStyle Style);

internal sealed record QuadrantPointStyle(double? Radius = null, string? Fill = null, string? Stroke = null, double? StrokeWidth = null)
{
    public QuadrantPointStyle Merge(QuadrantPointStyle? other) => other is null
        ? this
        : new(other.Radius ?? Radius, other.Fill ?? Fill, other.Stroke ?? Stroke, other.StrokeWidth ?? StrokeWidth);
}

internal sealed class QuadrantModel : IDiagramModel
{
    public string? Title { get; set; }

    public string XLeft { get; set; } = "";

    public string XRight { get; set; } = "";

    public string YBottom { get; set; } = "";

    public string YTop { get; set; } = "";

    public string[] Quadrants { get; } = ["", "", "", ""];

    public List<QuadrantPoint> Points { get; } = [];

    public Dictionary<string, QuadrantPointStyle> Classes { get; } = new(StringComparer.Ordinal);

    public DiagramScene Render(RenderContext context, DiagramInfo info) => QuadrantRenderer.Render(this, context, info);
}

internal static partial class QuadrantParser
{
    [GeneratedRegex(@"^(?<label>""[^""]*""|[^:\[]+?)(?::::(?<class>[A-Za-z_][\w-]*))?\s*:\s*\[\s*(?<x>[-+]?\d*\.?\d+)\s*,\s*(?<y>[-+]?\d*\.?\d+)\s*\]\s*(?<style>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex PointLine();

    public static QuadrantModel Parse(Source source)
    {
        var model = new QuadrantModel();
        foreach (var line in source.Lines)
        {
            var text = line.Trimmed;
            if (StartsWithWord(text, "title"))
            {
                model.Title = text[5..].Trim();
            }
            else if (text.StartsWith("x-axis", StringComparison.Ordinal))
            {
                (model.XLeft, model.XRight) = AxisLabels(text[6..]);
            }
            else if (text.StartsWith("y-axis", StringComparison.Ordinal))
            {
                (model.YBottom, model.YTop) = AxisLabels(text[6..]);
            }
            else if (text.StartsWith("quadrant-", StringComparison.Ordinal) && text.Length > 10 && text[9] is >= '1' and <= '4')
            {
                model.Quadrants[text[9] - '1'] = Unquote(text[10..].Trim());
            }
            else if (StartsWithWord(text, "classDef"))
            {
                var rest = text[8..].Trim();
                var space = rest.IndexOfAny([' ', '\t']);
                if (space <= 0)
                {
                    throw Error(line, "classDef needs a class name followed by styles.");
                }

                model.Classes[rest[..space]] = ParseStyle(rest[space..], line);
            }
            else
            {
                var match = PointLine().Match(text);
                if (!match.Success)
                {
                    throw Error(line, $"Unrecognized statement '{Source.Truncate(text)}'; a point is written Name: [x, y].");
                }

                if (!TryNumber(match.Groups["x"].Value, out var x) || !TryNumber(match.Groups["y"].Value, out var y) || x is < 0 or > 1 || y is < 0 or > 1)
                {
                    throw new MermaidParseException(MermaidErrorCode.InvalidValue, "Point coordinates must be between 0 and 1.", line.Number);
                }

                CheckCount(model.Points.Count + 1, MermaidEngine.MaxElements, line.Number, "points");
                var className = match.Groups["class"].Success ? match.Groups["class"].Value : null;
                model.Points.Add(new QuadrantPoint(Unquote(match.Groups["label"].Value.Trim()), x, y, className, ParseStyle(match.Groups["style"].Value, line)));
            }
        }

        return model;
    }

    private static (string, string) AxisLabels(string text)
    {
        var arrow = text.IndexOf("-->", StringComparison.Ordinal);
        return arrow < 0 ? (Unquote(text.Trim()), "") : (Unquote(text[..arrow].Trim()), Unquote(text[(arrow + 3)..].Trim()));
    }

    private static QuadrantPointStyle ParseStyle(string text, SourceLine line)
    {
        var style = new QuadrantPointStyle();
        foreach (var part in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = part.IndexOf(':');
            if (colon <= 0)
            {
                throw Error(line, $"Invalid point style '{Source.Truncate(part)}'.");
            }

            var key = part[..colon].Trim();
            var value = part[(colon + 1)..].Trim();
            var number = value.EndsWith("px", StringComparison.Ordinal) ? value[..^2] : value;
            style = key switch
            {
                "radius" when TryNumber(number, out var r) && r >= 0 => style with { Radius = Math.Min(r, 100) },
                "color" when Color.TryParse(value, out var fill) => style with { Fill = fill.ToString() },
                "stroke-color" when Color.TryParse(value, out var stroke) => style with { Stroke = stroke.ToString() },
                "stroke-width" when TryNumber(number, out var w) && w >= 0 => style with { StrokeWidth = Math.Min(w, 50) },
                _ => throw Error(line, $"Invalid point style '{Source.Truncate(part)}'.")
            };
        }

        return style;
    }
}

internal static class QuadrantRenderer
{
    public static DiagramScene Render(QuadrantModel model, RenderContext context, DiagramInfo info)
    {
        var theme = context.Theme;
        var config = context.Config;
        double N(string key, double fallback) => config.Number("quadrantChart", key, fallback);
        var width = N("chartWidth", 500);
        var height = N("chartHeight", 500);
        var titleSize = N("titleFontSize", 20);
        var titlePadding = N("titlePadding", 10);
        var padding = N("quadrantPadding", 5);
        var xLabelPadding = N("xAxisLabelPadding", 5);
        var yLabelPadding = N("yAxisLabelPadding", 5);
        var xLabelSize = N("xAxisLabelFontSize", 16);
        var yLabelSize = N("yAxisLabelFontSize", 16);
        var quadrantLabelSize = N("quadrantLabelFontSize", 16);
        var quadrantTextTop = N("quadrantTextTopPadding", 5);
        var pointTextPadding = N("pointTextPadding", 5);
        var pointLabelSize = N("pointLabelFontSize", 12);
        var pointRadius = N("pointRadius", 5);
        var internalBorder = N("quadrantInternalBorderStrokeWidth", 1);
        var externalBorder = N("quadrantExternalBorderStrokeWidth", 2);
        var hasPoints = model.Points.Count > 0;
        var xAxisTop = !hasPoints && (config.Text("quadrantChart", "xAxisPosition") ?? "top") == "top";
        var yAxisLeft = (config.Text("quadrantChart", "yAxisPosition") ?? "left") == "left";
        var showX = model.XLeft.Length > 0 || model.XRight.Length > 0;
        var showY = model.YBottom.Length > 0 || model.YTop.Length > 0;
        var title = info.Title;
        var xSpace = showX ? xLabelPadding * 2 + xLabelSize : 0;
        var ySpace = showY ? yLabelPadding * 2 + yLabelSize : 0;
        var titleSpace = string.IsNullOrWhiteSpace(title) ? 0 : titleSize + titlePadding * 2;

        var left = padding + (yAxisLeft ? ySpace : 0);
        var top = padding + (xAxisTop ? xSpace : 0) + titleSpace;
        var quadWidth = width - padding * 2 - ySpace;
        var quadHeight = height - padding * 2 - xSpace - titleSpace;
        var halfW = quadWidth / 2;
        var halfH = quadHeight / 2;
        var canvas = context.CreateCanvas();

        // Quadrant 1 is top right, then counter-clockwise.
        (double X, double Y)[] origins = [(left + halfW, top), (left, top), (left, top + halfH), (left + halfW, top + halfH)];
        for (var q = 0; q < 4; q++)
        {
            var (x, y) = origins[q];
            canvas.Rect(x, y, halfW, halfH, theme.Hex($"quadrant{q + 1}Fill"), null, 0);
            if (model.Quadrants[q].Length > 0)
            {
                var label = context.Measure(model.Quadrants[q], quadrantLabelSize, halfW - 10);
                var labelTop = hasPoints ? y + quadrantTextTop : y + (halfH - label.Height) / 2;
                canvas.Label(label, x + halfW / 2, labelTop, TextAnchor.Middle, theme.Hex($"quadrant{q + 1}TextFill"));
            }
        }

        var innerStroke = theme.Hex("quadrantInternalBorderStrokeFill");
        canvas.Line(left + halfW, top, left + halfW, top + quadHeight, innerStroke, internalBorder);
        canvas.Line(left, top + halfH, left + quadWidth, top + halfH, innerStroke, internalBorder);
        canvas.Rect(left, top, quadWidth, quadHeight, null, theme.Hex("quadrantExternalBorderStrokeFill"), externalBorder);

        if (showX)
        {
            var color = theme.Hex("quadrantXAxisTextFill");
            var labelTop = xAxisTop ? padding + titleSpace + xLabelPadding : top + quadHeight + xLabelPadding;
            var middle = model.XRight.Length > 0;
            var leftLabel = context.Measure(model.XLeft, xLabelSize);
            canvas.Label(leftLabel, middle ? left + halfW / 2 : left, labelTop, middle ? TextAnchor.Middle : TextAnchor.Start, color);
            if (middle)
            {
                canvas.Label(context.Measure(model.XRight, xLabelSize), left + halfW * 1.5, labelTop, TextAnchor.Middle, color);
            }
        }

        if (showY)
        {
            var color = theme.Hex("quadrantYAxisTextFill");
            // Rotated text turns around its baseline, which sits on the inner side of the label column.
            var baseline = (yAxisLeft ? padding + yLabelPadding : left + quadWidth + yLabelPadding) + yLabelSize * 0.8;
            var middle = model.YTop.Length > 0;
            canvas.Text(model.YBottom, baseline, middle ? top + halfH * 1.5 : top + quadHeight, yLabelSize, middle ? TextAnchor.Middle : TextAnchor.Start, color, rotation: -90);
            if (middle)
            {
                canvas.Text(model.YTop, baseline, top + halfH / 2, yLabelSize, TextAnchor.Middle, color, rotation: -90);
            }
        }

        if (titleSpace > 0)
        {
            canvas.Text(title!, width / 2, titlePadding + titleSize * 0.8, titleSize, TextAnchor.Middle, theme.Hex("quadrantTitleFill"));
        }

        var defaults = new QuadrantPointStyle(pointRadius, theme.Hex("quadrantPointFill"), null, 0);
        foreach (var point in model.Points)
        {
            var style = defaults.Merge(point.ClassName is null ? null : model.Classes.GetValueOrDefault(point.ClassName)).Merge(point.Style);
            var cx = left + point.X * quadWidth;
            var cy = top + (1 - point.Y) * quadHeight;
            var radius = style.Radius ?? pointRadius;
            canvas.Circle(cx, cy, radius, style.Fill, style.StrokeWidth > 0 ? style.Stroke ?? style.Fill : null, style.StrokeWidth ?? 0);
            canvas.Label(context.Measure(point.Label, pointLabelSize), cx, cy + radius + pointTextPadding, TextAnchor.Middle, theme.Hex("quadrantPointTextFill"));
        }

        return canvas.Finish(DiagramKind.Quadrant, info.Title, info.AccDescription, 0, null);
    }
}
