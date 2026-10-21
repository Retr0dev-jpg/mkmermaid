using System.Text.RegularExpressions;
using MkMermaid.Parsing;
using MkMermaid.Rendering;
using MkMermaid.Scene;
using static MkMermaid.Parsing.ParseHelpers;

namespace MkMermaid.Diagrams.Radar;

internal sealed record RadarAxis(string Id, string Label);

internal sealed record RadarCurve(string Id, string Label, IReadOnlyList<double> Values);

internal sealed class RadarModel : IDiagramModel
{
    public string? Title { get; set; }

    public List<RadarAxis> Axes { get; } = [];

    public List<RadarCurve> Curves { get; } = [];

    public double? Min { get; set; }

    public double? Max { get; set; }

    public int Ticks { get; set; } = 5;

    public bool ShowLegend { get; set; } = true;

    public bool Polygon { get; set; }

    public DiagramScene Render(RenderContext context, DiagramInfo info) => RadarRenderer.Render(this, context, info);
}

internal static partial class RadarParser
{
    [GeneratedRegex(@"^(?<id>[A-Za-z_][\w-]*)\s*(?:\[\s*""(?<label>[^""]*)""\s*\])?$", RegexOptions.CultureInvariant)]
    private static partial Regex AxisItem();

    [GeneratedRegex(@"^(?<id>[A-Za-z_][\w-]*)\s*(?:\[\s*""(?<label>[^""]*)""\s*\])?\s*\{(?<values>[^}]*)\}$", RegexOptions.CultureInvariant)]
    private static partial Regex CurveItem();

    public static RadarModel Parse(Source source)
    {
        var model = new RadarModel();
        var pending = new List<(string Id, string Label, string Values, int Line)>();
        foreach (var line in source.Lines)
        {
            var text = line.Trimmed;
            if (StartsWithWord(text, "title"))
            {
                model.Title = Unquote(text[5..].Trim());
            }
            else if (StartsWithWord(text, "axis"))
            {
                foreach (var part in SplitTopLevel(text[4..], ','))
                {
                    var match = AxisItem().Match(part.Trim());
                    if (!match.Success)
                    {
                        throw Error(line, $"Invalid axis '{Source.Truncate(part.Trim())}'; write id or id[\"Label\"].");
                    }

                    CheckCount(model.Axes.Count + 1, 100, line.Number, "axes");
                    var id = match.Groups["id"].Value;
                    model.Axes.Add(new RadarAxis(id, match.Groups["label"].Success ? match.Groups["label"].Value : id));
                }
            }
            else if (StartsWithWord(text, "curve"))
            {
                foreach (var part in SplitTopLevel(text[5..], ','))
                {
                    var match = CurveItem().Match(part.Trim());
                    if (!match.Success)
                    {
                        throw Error(line, $"Invalid curve '{Source.Truncate(part.Trim())}'; write id[\"Label\"]{{1, 2, 3}}.");
                    }

                    CheckCount(model.Curves.Count + pending.Count + 1, 100, line.Number, "curves");
                    var id = match.Groups["id"].Value;
                    pending.Add((id, match.Groups["label"].Success ? match.Groups["label"].Value : id, match.Groups["values"].Value, line.Number));
                }
            }
            else if (StartsWithWord(text, "max") || StartsWithWord(text, "min") || StartsWithWord(text, "ticks"))
            {
                var space = text.IndexOf(' ');
                if (space < 0 || !TryNumber(text[space..], out var value))
                {
                    throw Error(line, $"'{text.Split(' ')[0]}' needs a number.");
                }

                switch (text[..space])
                {
                    case "max":
                        model.Max = value;
                        break;
                    case "min":
                        model.Min = value;
                        break;
                    default:
                        model.Ticks = (int)Math.Clamp(value, 1, 50);
                        break;
                }
            }
            else if (StartsWithWord(text, "showLegend"))
            {
                model.ShowLegend = text[10..].Trim() != "false";
            }
            else if (StartsWithWord(text, "graticule"))
            {
                model.Polygon = text[9..].Trim() == "polygon";
            }
            else
            {
                throw Error(line, $"Unrecognized statement '{Source.Truncate(text)}'.");
            }
        }

        // Values may name axes, so curves are resolved once every axis is known.
        foreach (var (id, label, values, number) in pending)
        {
            model.Curves.Add(new RadarCurve(id, label, Values(model, values, number)));
        }

        return model;
    }

    private static List<double> Values(RadarModel model, string body, int line)
    {
        var parts = body.Trim().Length == 0 ? [] : SplitTopLevel(body, ',').Select(p => p.Trim()).ToList();
        if (parts.Count > 0 && parts.All(p => p.Contains(':', StringComparison.Ordinal)))
        {
            var byAxis = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var part in parts)
            {
                var colon = part.IndexOf(':');
                var axis = part[..colon].Trim();
                if (!model.Axes.Any(a => a.Id == axis))
                {
                    throw new MermaidParseException(MermaidErrorCode.UnknownReference, $"Curve value refers to unknown axis '{Source.Truncate(axis)}'.", line);
                }

                byAxis[axis] = Number(part[(colon + 1)..], line);
            }

            return model.Axes.Select(a => byAxis.TryGetValue(a.Id, out var v) ? v : 0).ToList();
        }

        var values = parts.Select(p => Number(p, line)).ToList();
        if (values.Count != model.Axes.Count)
        {
            throw new MermaidParseException(MermaidErrorCode.InvalidValue, $"A curve has {values.Count} values but the chart has {model.Axes.Count} axes.", line);
        }

        return values;
    }

    private static double Number(string text, int line) => TryNumber(text, out var value)
        ? value
        : throw new MermaidParseException(MermaidErrorCode.InvalidValue, $"'{Source.Truncate(text.Trim())}' is not a number.", line);
}

internal static class RadarRenderer
{
    public static DiagramScene Render(RadarModel model, RenderContext context, DiagramInfo info)
    {
        var theme = context.Theme;
        var config = context.Config;
        double N(string key, double fallback) => config.Number("radar", key, fallback);
        var width = N("width", 600);
        var height = N("height", 600);
        var scale = N("axisScaleFactor", 1);
        var labelFactor = N("axisLabelFactor", 1.05);
        var tension = N("curveTension", 0.17);
        var radius = Math.Min(width, height) / 2;
        var max = model.Max ?? (model.Curves.Count == 0 ? 1 : model.Curves.SelectMany(c => c.Values).DefaultIfEmpty(1).Max());
        var min = model.Min ?? 0;
        if (max <= min)
        {
            max = min + 1;
        }

        var canvas = context.CreateCanvas();
        var n = model.Axes.Count;
        double Angle(int i) => 2 * Math.PI * i / Math.Max(n, 1) - Math.PI / 2;

        var graticule = theme.Hex("radar.graticuleColor", "#DEDEDE");
        var graticuleWidth = theme.Number("radar.graticuleStrokeWidth", 1);
        var graticuleOpacity = theme.Number("radar.graticuleOpacity", 0.3);
        for (var t = 0; t < model.Ticks; t++)
        {
            var r = radius * (t + 1) / model.Ticks;
            var shape = model.Polygon && n > 2
                ? PathGeometry.Polygon(Enumerable.Range(0, n).Select(i => (r * Math.Cos(Angle(i)), r * Math.Sin(Angle(i)))))
                : PathGeometry.Ellipse(0, 0, r, r);
            canvas.Shape(shape, graticule, null, 0, opacity: graticuleOpacity);
            canvas.Shape(shape, null, graticule, graticuleWidth);
        }

        var axisColor = theme.Hex("radar.axisColor", theme.Hex("lineColor"));
        var axisWidth = theme.Number("radar.axisStrokeWidth", 2);
        var labelSize = theme.Number("radar.axisLabelFontSize", 12);
        for (var i = 0; i < n; i++)
        {
            var (c, s) = (Math.Cos(Angle(i)), Math.Sin(Angle(i)));
            canvas.Line(0, 0, radius * scale * c, radius * scale * s, axisColor, axisWidth);
            canvas.Text(model.Axes[i].Label, radius * labelFactor * c, radius * labelFactor * s + labelSize * 0.35, labelSize, TextAnchor.Middle, theme.Hex("textColor"));
        }

        var opacity = theme.Number("radar.curveOpacity", 0.5);
        var strokeWidth = theme.Number("radar.curveStrokeWidth", 2);
        for (var k = 0; k < model.Curves.Count; k++)
        {
            var curve = model.Curves[k];
            var color = theme.Hex("cScale" + k % 12);
            var points = curve.Values.Select((v, i) =>
            {
                var r = Math.Clamp((v - min) / (max - min), 0, 1) * radius;
                return new Layout.Point(r * Math.Cos(Angle(i)), r * Math.Sin(Angle(i)));
            }).ToList();
            var path = model.Polygon ? PathGeometry.Polygon(points.Select(p => (p.X, p.Y))) : ClosedCurve(points, tension);
            canvas.Shape(path, color, null, 0, opacity: opacity);
            canvas.Shape(path, null, color, strokeWidth);
        }

        if (model.ShowLegend && model.Curves.Count > 0)
        {
            var box = theme.Number("radar.legendBoxSize", 12);
            var size = theme.Number("radar.legendFontSize", 12);
            var x = (width / 2 + N("marginRight", 50)) * 3 / 4;
            var y = -(height / 2 + N("marginTop", 50)) * 3 / 4;
            for (var k = 0; k < model.Curves.Count; k++)
            {
                var color = theme.Hex("cScale" + k % 12);
                canvas.Rect(x, y + k * 20, box, box, color, color, 1, opacity: opacity);
                canvas.Text(model.Curves[k].Label, x + box + 5, y + k * 20 + box / 2 + size * 0.35, size, TextAnchor.Start, theme.Hex("textColor"));
            }
        }

        canvas.Title(info.Title, theme.Hex("titleColor", theme.Hex("textColor")));
        return canvas.Finish(DiagramKind.Radar, info.Title, info.AccDescription, N("marginTop", 50) / 2, null);
    }

    /// <summary>Closed cardinal spline through the points (the curve Mermaid draws with <c>curveTension</c>).</summary>
    private static PathGeometry ClosedCurve(List<Layout.Point> points, double tension)
    {
        var path = new PathGeometry();
        if (points.Count == 0)
        {
            return path;
        }

        path.MoveTo(points[0].X, points[0].Y);
        var count = points.Count;
        for (var i = 0; i < count; i++)
        {
            var p0 = points[(i - 1 + count) % count];
            var p1 = points[i];
            var p2 = points[(i + 1) % count];
            var p3 = points[(i + 2) % count];
            path.CubicTo(
                p1.X + (p2.X - p0.X) * tension, p1.Y + (p2.Y - p0.Y) * tension,
                p2.X - (p3.X - p1.X) * tension, p2.Y - (p3.Y - p1.Y) * tension,
                p2.X, p2.Y);
        }

        return path.Close();
    }
}
