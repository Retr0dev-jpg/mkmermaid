using System.Globalization;
using System.Text.RegularExpressions;
using MkMermaid.Parsing;
using MkMermaid.Rendering;
using MkMermaid.Scene;
using static MkMermaid.Parsing.ParseHelpers;

namespace MkMermaid.Diagrams.Pie;

internal sealed class PieModel : IDiagramModel
{
    public string? Title { get; set; }

    public bool ShowData { get; set; }

    public List<(string Label, double Value)> Slices { get; } = [];

    public DiagramScene Render(RenderContext context, DiagramInfo info) => PieRenderer.Render(this, context, info);
}

internal static partial class PieParser
{
    [GeneratedRegex(@"^(?:""(?<label>[^""]*)""|'(?<label>[^']*)')\s*:\s*(?<value>[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex SliceLine();

    public static PieModel Parse(Source source)
    {
        var model = new PieModel();
        var header = source.HeaderRest;
        if (header.StartsWith("showData", StringComparison.Ordinal))
        {
            model.ShowData = true;
            header = header[8..].Trim();
        }

        if (header.StartsWith("title", StringComparison.Ordinal))
        {
            model.Title = header[5..].Trim();
        }

        foreach (var line in source.Lines)
        {
            var text = line.Trimmed;
            if (StartsWithWord(text, "title"))
            {
                model.Title = text[5..].Trim();
                continue;
            }

            if (text == "showData")
            {
                model.ShowData = true;
                continue;
            }

            var match = SliceLine().Match(text);
            if (!match.Success)
            {
                throw Error(line, $"Unrecognized statement '{Source.Truncate(text)}'; a slice is written \"Label\" : value.");
            }

            var value = double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
            if (value < 0 || !double.IsFinite(value))
            {
                throw new MermaidParseException(MermaidErrorCode.InvalidValue, $"\"{Source.Truncate(match.Groups["label"].Value)}\" has a negative value; pie slices must be zero or positive.", line.Number);
            }

            CheckCount(model.Slices.Count + 1, MermaidEngine.MaxElements, line.Number, "slices");
            var label = match.Groups["label"].Value;
            var existing = model.Slices.FindIndex(s => s.Label == label);
            if (existing >= 0)
            {
                model.Slices[existing] = (label, value);
            }
            else
            {
                model.Slices.Add((label, value));
            }
        }

        return model;
    }
}

internal static class PieRenderer
{
    public static DiagramScene Render(PieModel model, RenderContext context, DiagramInfo info)
    {
        var theme = context.Theme;
        var config = context.Config;
        var radius = 185.0;
        var textPosition = Math.Clamp(config.Number("pie", "textPosition", 0.75), 0, 1);
        var canvas = context.CreateCanvas();
        var total = model.Slices.Sum(s => s.Value);
        // Largest slice first, clockwise from 12 o'clock; the legend keeps the source order.
        var slices = model.Slices.Where(s => s.Value > 0).OrderByDescending(s => s.Value).ToList();
        var sectionText = theme.Hex("pieSectionTextColor", theme.Hex("textColor"));
        var sectionSize = theme.Number("pieSectionTextSize", 17);
        var stroke = theme.Hex("pieStrokeColor", "#000000");
        var strokeWidth = theme.Number("pieStrokeWidth", 2);
        var opacity = theme.Number("pieOpacity", 0.7);
        var colors = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < model.Slices.Count; i++)
        {
            colors[model.Slices[i].Label] = theme.Hex("pie" + (i % 12 + 1));
        }

        var angle = -Math.PI / 2;
        foreach (var (label, value) in slices)
        {
            var sweep = value / total * 2 * Math.PI;
            var fill = colors[label];
            if (slices.Count == 1)
            {
                canvas.Shape(PathGeometry.Ellipse(0, 0, radius, radius), fill, stroke, strokeWidth, opacity: opacity);
            }
            else
            {
                var path = new PathGeometry().MoveTo(0, 0).LineTo(radius * Math.Cos(angle), radius * Math.Sin(angle))
                    .ArcTo(0, 0, radius, radius, angle, sweep).Close();
                canvas.Shape(path, fill, stroke, strokeWidth, opacity: opacity);
            }

            var mid = angle + sweep / 2;
            var percent = (value / total * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
            var r = radius * textPosition;
            canvas.Text(percent, r * Math.Cos(mid), r * Math.Sin(mid) + sectionSize * 0.35, sectionSize, TextAnchor.Middle, sectionText);
            angle += sweep;
        }

        canvas.Circle(0, 0, radius + theme.Number("pieOuterStrokeWidth", 2) / 2, null, theme.Hex("pieOuterStrokeColor", "#000000"), theme.Number("pieOuterStrokeWidth", 2));

        // Legend.
        var legendSize = theme.Number("pieLegendTextSize", 17);
        var legendColor = theme.Hex("pieLegendTextColor", theme.Hex("textColor"));
        const double box = 18;
        const double spacing = 4;
        var legendHeight = model.Slices.Count * (box + spacing);
        var y = -legendHeight / 2;
        var x = radius + 40;
        foreach (var (label, value) in model.Slices)
        {
            canvas.Rect(x, y, box, box, colors[label], colors[label]);
            var text = model.ShowData ? $"{label} [{value.ToString("0.##", CultureInfo.InvariantCulture)}]" : label;
            canvas.Text(text, x + box + spacing * 2, y + box / 2 + legendSize * 0.35, legendSize, TextAnchor.Start, legendColor);
            y += box + spacing;
        }

        if (!string.IsNullOrWhiteSpace(info.Title))
        {
            var size = theme.Number("pieTitleTextSize", 25);
            canvas.Text(info.Title, 0, -radius - 25, size, TextAnchor.Middle, theme.Hex("pieTitleTextColor", theme.Hex("textColor")));
        }

        return canvas.Finish(DiagramKind.Pie, info.Title, info.AccDescription, 10, null);
    }
}
