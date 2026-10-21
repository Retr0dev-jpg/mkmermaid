using System.Globalization;
using MkMermaid.Parsing;
using MkMermaid.Rendering;
using MkMermaid.Scene;
using MkMermaid.Theming;
using static MkMermaid.Parsing.ParseHelpers;

namespace MkMermaid.Diagrams.XyChart;

internal enum XyPlotKind
{
    Bar,
    Line
}

internal sealed record XyPlot(XyPlotKind Kind, string? Name, IReadOnlyList<double> Values);

internal sealed class XyAxis
{
    public string? Title { get; set; }

    public List<string>? Categories { get; set; }

    public double? Min { get; set; }

    public double? Max { get; set; }
}

internal sealed class XyChartModel : IDiagramModel
{
    public string? Title { get; set; }

    public bool Horizontal { get; set; }

    public XyAxis X { get; } = new();

    public XyAxis Y { get; } = new();

    public List<XyPlot> Plots { get; } = [];

    public DiagramScene Render(RenderContext context, DiagramInfo info) => XyChartRenderer.Render(this, context, info);
}

internal static class XyChartParser
{
    public static XyChartModel Parse(Source source)
    {
        var model = new XyChartModel { Horizontal = source.HeaderRest.Trim() == "horizontal" };
        foreach (var line in source.Lines)
        {
            var text = line.Trimmed;
            if (StartsWithWord(text, "title"))
            {
                model.Title = Unquote(text[5..].Trim());
            }
            else if (text.StartsWith("x-axis", StringComparison.Ordinal))
            {
                ParseAxis(model.X, text[6..].Trim(), line, allowCategories: true);
            }
            else if (text.StartsWith("y-axis", StringComparison.Ordinal))
            {
                ParseAxis(model.Y, text[6..].Trim(), line, allowCategories: false);
            }
            else if (StartsWithWord(text, "bar") || StartsWithWord(text, "line"))
            {
                var kind = text[0] == 'b' ? XyPlotKind.Bar : XyPlotKind.Line;
                var rest = text[(kind == XyPlotKind.Bar ? 3 : 4)..].Trim();
                var name = TakeTitle(ref rest);
                var values = ParseList(rest, line).Select(v => TryNumber(v, out var n)
                    ? n
                    : throw new MermaidParseException(MermaidErrorCode.InvalidValue, $"'{Source.Truncate(v)}' is not a number.", line.Number)).ToList();
                CheckCount(values.Count, MermaidEngine.MaxElements, line.Number, "values");
                CheckCount(model.Plots.Count + 1, 100, line.Number, "data series");
                model.Plots.Add(new XyPlot(kind, name, values));
            }
            else
            {
                throw Error(line, $"Unrecognized statement '{Source.Truncate(text)}'.");
            }
        }

        return model;
    }

    private static void ParseAxis(XyAxis axis, string rest, SourceLine line, bool allowCategories)
    {
        axis.Title = TakeTitle(ref rest);
        if (rest.Length == 0)
        {
            return;
        }

        if (rest[0] == '[')
        {
            if (!allowCategories)
            {
                throw Error(line, "The y-axis takes a numeric range (min --> max), not categories.");
            }

            axis.Categories = ParseList(rest, line).Select(Unquote).ToList();
            CheckCount(axis.Categories.Count, MermaidEngine.MaxElements, line.Number, "categories");
            return;
        }

        var arrow = rest.IndexOf("-->", StringComparison.Ordinal);
        if (arrow < 0 || !TryNumber(rest[..arrow], out var min) || !TryNumber(rest[(arrow + 3)..], out var max))
        {
            throw Error(line, "An axis range is written min --> max.");
        }

        (axis.Min, axis.Max) = (min, max);
    }

    /// <summary>Reads a quoted title or a single word that is not the start of a list or range.</summary>
    private static string? TakeTitle(ref string rest)
    {
        if (rest.Length > 0 && rest[0] == '"')
        {
            var end = rest.IndexOf('"', 1);
            if (end > 0)
            {
                var title = rest[1..end];
                rest = rest[(end + 1)..].Trim();
                return title;
            }
        }

        if (rest.Length == 0 || rest[0] == '[' || char.IsDigit(rest[0]) || rest[0] is '-' or '+' or '.')
        {
            return null;
        }

        var space = rest.IndexOfAny([' ', '\t']);
        var word = space < 0 ? rest : rest[..space];
        rest = space < 0 ? "" : rest[space..].Trim();
        return word;
    }

    private static List<string> ParseList(string text, SourceLine line)
    {
        var t = text.Trim();
        if (t.Length < 2 || t[0] != '[' || t[^1] != ']')
        {
            throw Error(line, "Expected a list in square brackets, for example [1, 2, 3].");
        }

        var inner = t[1..^1].Trim();
        return inner.Length == 0 ? [] : SplitTopLevel(inner, ',').Select(s => s.Trim()).ToList();
    }
}

internal static class XyChartRenderer
{
    private sealed record Tick(double Position, string Label);

    public static DiagramScene Render(XyChartModel model, RenderContext context, DiagramInfo info)
    {
        var theme = context.Theme;
        var config = context.Config;
        double N(string key, double fallback) => config.Number("xyChart", key, fallback);
        var width = N("width", 700);
        var height = N("height", 500);
        var titleSize = N("titleFontSize", 20);
        var titlePadding = N("titlePadding", 10);
        var labelSize = config.GetNumber("xyChart.xAxis.labelFontSize", 14);
        var labelPadding = config.GetNumber("xyChart.xAxis.labelPadding", 5);
        var axisTitleSize = config.GetNumber("xyChart.xAxis.titleFontSize", 16);
        var axisTitlePadding = config.GetNumber("xyChart.xAxis.titlePadding", 5);
        var tickLength = config.GetNumber("xyChart.xAxis.tickLength", 5);
        var tickWidth = config.GetNumber("xyChart.xAxis.tickWidth", 2);
        var axisLineWidth = config.GetNumber("xyChart.xAxis.axisLineWidth", 2);
        var horizontal = model.Horizontal || config.Text("xyChart", "chartOrientation") == "horizontal";
        var showDataLabel = config.Flag("xyChart", "showDataLabel", false);
        var palette = (theme.Get("xyChart.plotColorPalette") ?? "#FFF4DD,#FFD8B1,#FFA07A,#ECEFF1,#D6DBDF,#C3E0A9,#FFB6A4,#FFD74D,#738FA7,#FFFFF0")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(c => Color.TryParse(c, out var parsed) ? parsed.ToString() : null)
            .OfType<string>()
            .ToArray();
        var canvas = context.CreateCanvas();
        canvas.Rect(0, 0, width, height, theme.Hex("xyChart.backgroundColor"), null, 0);

        var count = model.Plots.Count == 0 ? 0 : model.Plots.Max(p => p.Values.Count);
        var categories = model.X.Categories
            ?? (model.X.Min is not null && model.X.Max is not null
                ? null
                : Enumerable.Range(1, Math.Max(count, 1)).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList());
        var values = model.Plots.SelectMany(p => p.Values).ToList();
        var min = model.Y.Min ?? (values.Count == 0 ? 0 : values.Min());
        var max = model.Y.Max ?? (values.Count == 0 ? 1 : values.Max());
        if (max <= min)
        {
            max = min + 1;
        }

        var valueTicks = NiceTicks(min, max);
        var top = string.IsNullOrWhiteSpace(info.Title) ? 0 : titleSize + 2 * titlePadding;
        if (top > 0)
        {
            canvas.Text(info.Title!, width / 2, titlePadding + titleSize * 0.8, titleSize, TextAnchor.Middle, theme.Hex("xyChart.titleColor"));
        }

        // The category axis runs along the bottom (vertical chart) or the left side (horizontal chart).
        var categoryLabels = categories ?? NiceTicks(model.X.Min!.Value, model.X.Max!.Value).Select(Format).ToList();
        var valueLabels = valueTicks.Select(Format).ToList();
        var leftLabels = horizontal ? categoryLabels : valueLabels;
        var leftTitle = horizontal ? model.X.Title : model.Y.Title;
        var bottomTitle = horizontal ? model.Y.Title : model.X.Title;
        var leftWidth = (leftLabels.Count == 0 ? 0 : leftLabels.Max(l => context.TextWidth(l, labelSize))) + 2 * labelPadding + tickLength + axisLineWidth
            + (string.IsNullOrEmpty(leftTitle) ? 0 : axisTitleSize + 2 * axisTitlePadding);
        var bottomHeight = labelSize + 2 * labelPadding + tickLength + axisLineWidth
            + (string.IsNullOrEmpty(bottomTitle) ? 0 : axisTitleSize + 2 * axisTitlePadding);
        var plot = new Layout.Rect(leftWidth, top + labelSize / 2, Math.Max(10, width - leftWidth - labelSize), Math.Max(10, height - top - bottomHeight - labelSize / 2));

        double ValueAt(double v) => horizontal
            ? plot.X + (v - min) / (max - min) * plot.Width
            : plot.Bottom - (v - min) / (max - min) * plot.Height;
        var categoryLength = horizontal ? plot.Height : plot.Width;
        var slots = categories?.Count ?? Math.Max(count, 1);
        var step = categoryLength / Math.Max(slots, 1);
        double CategoryAt(int i)
        {
            if (categories is null)
            {
                var lo = model.X.Min!.Value;
                var hi = model.X.Max!.Value;
                var value = count <= 1 ? lo : lo + i * (hi - lo) / (count - 1);
                var t = hi == lo ? 0.5 : (value - lo) / (hi - lo);
                return horizontal ? plot.Y + t * plot.Height : plot.X + t * plot.Width;
            }

            return horizontal ? plot.Y + step * (i + 0.5) : plot.X + step * (i + 0.5);
        }

        var categoryTicks = categories is null
            ? NiceTicks(model.X.Min!.Value, model.X.Max!.Value).Select(v =>
            {
                var lo = model.X.Min!.Value;
                var hi = model.X.Max!.Value;
                var t = hi == lo ? 0.5 : (v - lo) / (hi - lo);
                return new Tick(horizontal ? plot.Y + t * plot.Height : plot.X + t * plot.Width, Format(v));
            }).ToList()
            : categories.Select((c, i) => new Tick(CategoryAt(i), c)).ToList();
        var valueTickList = valueTicks.Select(v => new Tick(ValueAt(v), Format(v))).ToList();

        // Bars grow from the axis minimum, as in Mermaid; series keep their palette slot in source order.
        var barWidth = step * 0.95;
        var from = ValueAt(min);
        var index = 0;
        foreach (var plotData in model.Plots)
        {
            var color = palette.Length == 0 ? "#888888" : palette[index++ % palette.Length];
            if (plotData.Kind == XyPlotKind.Bar)
            {
                for (var i = 0; i < plotData.Values.Count; i++)
                {
                    var c = CategoryAt(i);
                    var v = ValueAt(Math.Clamp(plotData.Values[i], min, max));
                    if (horizontal)
                    {
                        canvas.Rect(from, c - barWidth / 2, Math.Max(0, v - from), barWidth, color, null, 0);
                    }
                    else
                    {
                        canvas.Rect(c - barWidth / 2, v, barWidth, Math.Max(0, from - v), color, null, 0);
                    }

                    if (showDataLabel)
                    {
                        var label = Format(plotData.Values[i]);
                        if (horizontal)
                        {
                            canvas.Text(label, v - 4, c + labelSize * 0.35, labelSize, TextAnchor.End, theme.Hex("xyChart.titleColor"));
                        }
                        else
                        {
                            canvas.Text(label, c, v + labelSize + 2, labelSize, TextAnchor.Middle, theme.Hex("xyChart.titleColor"));
                        }
                    }
                }
            }
            else if (plotData.Values.Count > 0)
            {
                var path = new PathGeometry();
                for (var i = 0; i < plotData.Values.Count; i++)
                {
                    var c = CategoryAt(i);
                    var v = ValueAt(plotData.Values[i]);
                    var (x, y) = horizontal ? (v, c) : (c, v);
                    if (i == 0)
                    {
                        path.MoveTo(x, y);
                    }
                    else
                    {
                        path.LineTo(x, y);
                    }
                }

                canvas.Shape(path, null, color, 2);
            }
        }

        DrawAxes(canvas, context, plot, horizontal ? valueTickList : categoryTicks, horizontal ? categoryTicks : valueTickList, bottomTitle, leftTitle,
            labelSize, labelPadding, axisTitleSize, axisTitlePadding, tickLength, tickWidth, axisLineWidth, height);
        return canvas.Finish(DiagramKind.XyChart, info.Title, info.AccDescription, 0, null);
    }

    private static void DrawAxes(Canvas canvas, RenderContext context, Layout.Rect plot, List<Tick> bottom, List<Tick> left, string? bottomTitle, string? leftTitle,
        double labelSize, double labelPadding, double titleSize, double titlePadding, double tickLength, double tickWidth, double lineWidth, double height)
    {
        var theme = context.Theme;
        var axisY = plot.Bottom + lineWidth / 2;
        canvas.Line(plot.X - lineWidth / 2, axisY, plot.Right, axisY, theme.Hex("xyChart.xAxisLineColor"), lineWidth);
        foreach (var tick in bottom)
        {
            canvas.Line(tick.Position, axisY, tick.Position, axisY + tickLength + lineWidth / 2, theme.Hex("xyChart.xAxisTickColor"), tickWidth);
            canvas.Text(tick.Label, tick.Position, axisY + lineWidth / 2 + tickLength + labelPadding + labelSize * 0.8, labelSize, TextAnchor.Middle, theme.Hex("xyChart.xAxisLabelColor"));
        }

        if (!string.IsNullOrEmpty(bottomTitle))
        {
            canvas.Text(bottomTitle, plot.CenterX, height - titlePadding - titleSize * 0.2, titleSize, TextAnchor.Middle, theme.Hex("xyChart.xAxisTitleColor"));
        }

        var axisX = plot.X - lineWidth / 2;
        canvas.Line(axisX, plot.Y, axisX, plot.Bottom + lineWidth, theme.Hex("xyChart.yAxisLineColor"), lineWidth);
        foreach (var tick in left)
        {
            canvas.Line(axisX - lineWidth / 2 - tickLength, tick.Position, axisX, tick.Position, theme.Hex("xyChart.yAxisTickColor"), tickWidth);
            canvas.Text(tick.Label, axisX - lineWidth / 2 - tickLength - labelPadding, tick.Position + labelSize * 0.35, labelSize, TextAnchor.End, theme.Hex("xyChart.yAxisLabelColor"));
        }

        if (!string.IsNullOrEmpty(leftTitle))
        {
            canvas.Text(leftTitle, titlePadding + titleSize * 0.8, plot.CenterY, titleSize, TextAnchor.Middle, theme.Hex("xyChart.yAxisTitleColor"), rotation: -90);
        }
    }

    /// <summary>Tick values inside [min, max] with a 1, 2 or 5 step, as d3's linear scale produces for ten ticks.</summary>
    internal static List<double> NiceTicks(double min, double max, int count = 10)
    {
        if (!(max > min) || !double.IsFinite(max - min))
        {
            return [min];
        }

        var raw = (max - min) / count;
        var power = Math.Floor(Math.Log10(raw));
        var error = raw / Math.Pow(10, power);
        var factor = error >= Math.Sqrt(50) ? 10 : error >= Math.Sqrt(10) ? 5 : error >= Math.Sqrt(2) ? 2 : 1;
        var step = factor * Math.Pow(10, power);
        var first = Math.Ceiling(min / step - 1e-9);
        var last = Math.Floor(max / step + 1e-9);
        var ticks = new List<double>();
        for (var i = first; i <= last && ticks.Count < 50; i++)
        {
            ticks.Add(Math.Round(i * step, 10));
        }

        return ticks;
    }

    private static string Format(double value) => (value == 0 ? 0 : value).ToString("0.##########", CultureInfo.InvariantCulture);
}
