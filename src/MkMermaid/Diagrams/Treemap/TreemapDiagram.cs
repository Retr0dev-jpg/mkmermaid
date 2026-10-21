using System.Globalization;
using System.Text.RegularExpressions;
using MkMermaid.Layout;
using MkMermaid.Parsing;
using MkMermaid.Rendering;
using MkMermaid.Scene;
using static MkMermaid.Parsing.ParseHelpers;

namespace MkMermaid.Diagrams.Treemap;

internal sealed class TreemapNode(string name, double? value, int indent)
{
    public string Name { get; } = name;

    public double? Value { get; } = value;

    public int Indent { get; } = indent;

    public string? ClassName { get; set; }

    public List<TreemapNode> Children { get; } = [];

    public double Total { get; set; }

    public Rect Bounds { get; set; }
}

internal sealed class TreemapModel : IDiagramModel
{
    public string? Title { get; set; }

    public TreemapNode Root { get; } = new("", null, -1);

    public Dictionary<string, Style> Classes { get; } = new(StringComparer.Ordinal);

    public DiagramScene Render(RenderContext context, DiagramInfo info) => TreemapRenderer.Render(this, context, info);
}

internal static partial class TreemapParser
{
    [GeneratedRegex(@"^(?:""(?<name>[^""]*)""|'(?<name>[^']*)')\s*(?::\s*(?<value>[-+]?\d[\d,]*\.?\d*(?:[eE][-+]?\d+)?))?\s*(?::::(?<class>[\w-]+))?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex NodeLine();

    public static TreemapModel Parse(Source source)
    {
        var model = new TreemapModel();
        var stack = new List<TreemapNode> { model.Root };
        var count = 0;
        foreach (var line in source.Lines)
        {
            var text = line.Trimmed;
            if (StartsWithWord(text, "title"))
            {
                model.Title = text[5..].Trim();
                continue;
            }

            if (StartsWithWord(text, "classDef"))
            {
                var rest = text[8..].Trim();
                var space = rest.IndexOfAny([' ', '\t']);
                if (space <= 0)
                {
                    throw Error(line, "classDef needs a class name followed by styles.");
                }

                model.Classes[rest[..space]] = Style.Parse(rest[space..].Trim().TrimEnd(';'));
                continue;
            }

            var match = NodeLine().Match(text);
            if (!match.Success)
            {
                throw Error(line, $"Unrecognized item '{Source.Truncate(text)}'; write \"Section\" or \"Leaf\": value.");
            }

            CheckCount(++count, MermaidEngine.MaxElements, line.Number, "items");
            double? value = null;
            if (match.Groups["value"].Success)
            {
                if (!TryNumber(match.Groups["value"].Value.Replace(",", "", StringComparison.Ordinal), out var v) || v < 0)
                {
                    throw new MermaidParseException(MermaidErrorCode.InvalidValue, "Treemap values must be zero or positive numbers.", line.Number);
                }

                value = v;
            }

            var node = new TreemapNode(match.Groups["name"].Value, value, line.Indent)
            {
                ClassName = match.Groups["class"].Success ? match.Groups["class"].Value : null
            };
            while (stack.Count > 1 && stack[^1].Indent >= node.Indent)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            stack[^1].Children.Add(node);
            stack.Add(node);
        }

        return model;
    }
}

/// <summary>Squarified treemap with d3's ratio, padding and descending sort, as Mermaid lays it out.</summary>
internal static class TreemapRenderer
{
    private const double HeaderHeight = 25;
    private const double SectionPadding = 10;
    private static readonly double Phi = (1 + Math.Sqrt(5)) / 2;

    public static DiagramScene Render(TreemapModel model, RenderContext context, DiagramInfo info)
    {
        var theme = context.Theme;
        var config = context.Config;
        var inner = config.Number("treemap", "padding", 10);
        var width = config.Number("treemap", "nodeWidth", 100) * 10;
        var height = config.Number("treemap", "nodeHeight", 40) * 10;
        var showValues = config.Flag("treemap", "showValues", true);
        var labelSize = config.Number("treemap", "labelFontSize", 14);
        var valueSize = config.Number("treemap", "valueFontSize", 12);
        var format = config.Text("treemap", "valueFormat") ?? ",";

        Sum(model.Root);
        model.Root.Bounds = new Rect(0, 0, width, height);
        Layout(model.Root, inner, isRoot: true);

        // Colors follow first appearance in breadth-first order, as d3's ordinal scale assigns them.
        var colorIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var queue = new Queue<TreemapNode>(model.Root.Children);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node.Children.Count > 0)
            {
                colorIndex.TryAdd(node.Name, colorIndex.Count % 12);
                foreach (var child in node.Children)
                {
                    queue.Enqueue(child);
                }
            }
        }

        var canvas = context.CreateCanvas();
        Draw(model.Root, null, canvas, context, model, colorIndex, showValues, labelSize, valueSize, format);
        canvas.Title(info.Title, theme.Hex("textColor"));
        return canvas.Finish(DiagramKind.Treemap, info.Title, info.AccDescription, config.Number("treemap", "diagramPadding", 8), null);
    }

    private static double Sum(TreemapNode node)
    {
        node.Total = node.Children.Count == 0 ? node.Value ?? 0 : node.Children.Sum(Sum);
        node.Children.Sort((a, b) => b.Total.CompareTo(a.Total));
        return node.Total;
    }

    private static void Layout(TreemapNode node, double inner, bool isRoot)
    {
        if (node.Children.Count == 0)
        {
            return;
        }

        var b = node.Bounds;
        var (top, side) = isRoot ? (0.0, 0.0) : (HeaderHeight + SectionPadding, SectionPadding);
        var half = inner / 2;

        // As in d3: lay children out in the padded box grown by half the inner padding, then shrink each child by it.
        var x0 = b.X + side - half;
        var y0 = b.Y + top - half;
        var x1 = b.Right - side + half;
        var y1 = b.Bottom - side + half;
        if (x1 < x0)
        {
            x0 = x1 = (x0 + x1) / 2;
        }

        if (y1 < y0)
        {
            y0 = y1 = (y0 + y1) / 2;
        }

        Squarify(node.Children, node.Total, x0, y0, x1, y1);
        foreach (var child in node.Children)
        {
            var c = child.Bounds;
            var w = Math.Max(0, c.Width - inner);
            var h = Math.Max(0, c.Height - inner);
            child.Bounds = new Rect(c.X + half, c.Y + half, w, h);
            Layout(child, inner, isRoot: false);
        }
    }

    private static void Squarify(List<TreemapNode> nodes, double value, double x0, double y0, double x1, double y1)
    {
        var i0 = 0;
        var n = nodes.Count;
        while (i0 < n)
        {
            var dx = x1 - x0;
            var dy = y1 - y0;
            var i1 = i0;
            double sum;
            do
            {
                sum = nodes[i1++].Total;
            }
            while (sum == 0 && i1 < n);

            var min = sum;
            var max = sum;
            var alpha = dx <= 0 || dy <= 0 || value <= 0 ? 0 : Math.Max(dy / dx, dx / dy) / (value * Phi);
            var beta = sum * sum * alpha;
            var minRatio = beta == 0 ? double.PositiveInfinity : Math.Max(max / beta, beta / min);
            for (; i1 < n; i1++)
            {
                var v = nodes[i1].Total;
                sum += v;
                min = Math.Min(min, v);
                max = Math.Max(max, v);
                beta = sum * sum * alpha;
                var ratio = beta == 0 || min == 0 ? double.PositiveInfinity : Math.Max(max / beta, beta / min);
                if (ratio > minRatio)
                {
                    sum -= v;
                    break;
                }

                minRatio = ratio;
            }

            var row = nodes.GetRange(i0, i1 - i0);
            if (dx < dy)
            {
                var yEnd = value > 0 ? y0 + dy * sum / value : y1;
                var k = sum > 0 ? dx / sum : 0;
                var x = x0;
                foreach (var node in row)
                {
                    node.Bounds = new Rect(x, y0, node.Total * k, yEnd - y0);
                    x += node.Total * k;
                }

                y0 = yEnd;
            }
            else
            {
                var xEnd = value > 0 ? x0 + dx * sum / value : x1;
                var k = sum > 0 ? dy / sum : 0;
                var y = y0;
                foreach (var node in row)
                {
                    node.Bounds = new Rect(x0, y, xEnd - x0, node.Total * k);
                    y += node.Total * k;
                }

                x0 = xEnd;
            }

            value -= sum;
            i0 = i1;
        }
    }

    private static void Draw(TreemapNode node, TreemapNode? parent, Canvas canvas, RenderContext context, TreemapModel model,
        Dictionary<string, int> colors, bool showValues, double labelSize, double valueSize, string format)
    {
        var theme = context.Theme;
        if (parent is not null)
        {
            var r = node.Bounds;
            var style = node.ClassName is null ? Style.None : model.Classes.GetValueOrDefault(node.ClassName) ?? Style.None;
            if (node.Children.Count > 0)
            {
                var index = colors[node.Name];
                var fill = Style.Resolve(style.Fill, theme.Hex("cScale" + index)) ?? "#00000000";
                canvas.Rect(r.X, r.Y, r.Width, r.Height, fill, null, 0, opacity: 0.6);
                canvas.Rect(r.X, r.Y, r.Width, r.Height, null, Style.Resolve(style.Stroke, theme.Hex("cScalePeer" + index)), style.StrokeWidth ?? 2);
                var color = style.Color ?? theme.Hex("cScaleLabel" + index);
                canvas.Text(Fit(node.Name, r.Width - 12, labelSize, context), r.X + 6, r.Y + HeaderHeight / 2 + labelSize * 0.35, labelSize, TextAnchor.Start, color, bold: true);
                if (showValues)
                {
                    canvas.Text(Format(node.Total, format), r.Right - 6, r.Y + HeaderHeight / 2 + valueSize * 0.35, valueSize, TextAnchor.End, color, italic: true);
                }
            }
            else if (r.Width > 0 && r.Height > 0)
            {
                var index = parent.Indent < 0 ? 0 : colors.GetValueOrDefault(parent.Name);
                var color = theme.Hex("cScale" + index);
                canvas.Rect(r.X, r.Y, r.Width, r.Height, Style.Resolve(style.Fill, color), null, 0, opacity: 0.3);
                canvas.Rect(r.X, r.Y, r.Width, r.Height, null, Style.Resolve(style.Stroke, color), style.StrokeWidth ?? 3);

                // Shrink the label until it fits, as Mermaid does, and drop it when the box is too small.
                var size = labelSize;
                while (size > 8 && context.TextWidth(node.Name, size) > r.Width - 8)
                {
                    size--;
                }

                var text = style.Color ?? theme.Hex("cScaleLabel" + index);
                var showValue = showValues && r.Height >= size + valueSize + 12;
                if (context.TextWidth(node.Name, size) <= r.Width - 8 && r.Height >= size + 4)
                {
                    var cy = r.CenterY - (showValue ? valueSize / 2 : 0);
                    canvas.Text(node.Name, r.CenterX, cy + size * 0.35, size, TextAnchor.Middle, text);
                    if (showValue)
                    {
                        canvas.Text(Format(node.Total, format), r.CenterX, cy + size * 0.35 + valueSize + 4, valueSize, TextAnchor.Middle, text);
                    }
                }
            }
        }

        foreach (var child in node.Children)
        {
            Draw(child, node, canvas, context, model, colors, showValues, labelSize, valueSize, format);
        }
    }

    private static string Fit(string text, double width, double size, RenderContext context)
    {
        if (context.TextWidth(text, size, bold: true) <= width)
        {
            return text;
        }

        for (var length = text.Length - 1; length > 0; length--)
        {
            var candidate = text[..length] + "…";
            if (context.TextWidth(candidate, size, bold: true) <= width)
            {
                return candidate;
            }
        }

        return "";
    }

    /// <summary>The d3-format subset Mermaid's examples use: "," (thousands), "$", ".Nf" and "%".</summary>
    private static string Format(double value, string format)
    {
        var currency = format.StartsWith('$');
        var spec = currency ? format[1..] : format;
        var percent = spec.EndsWith('%');
        if (percent)
        {
            spec = spec[..^1];
            value *= 100;
        }

        var grouping = spec.Contains(',', StringComparison.Ordinal);
        var decimals = 0;
        var dot = spec.IndexOf('.');
        if (dot >= 0)
        {
            _ = int.TryParse(spec[(dot + 1)..].TrimEnd('f'), NumberStyles.None, CultureInfo.InvariantCulture, out decimals);
        }

        var pattern = dot >= 0 ? (grouping ? "#,0." : "0.") + new string('0', Math.Clamp(decimals, 0, 10)) : grouping ? "#,0.##" : "0.##";
        if (pattern.EndsWith('.'))
        {
            pattern = pattern[..^1];
        }

        return (currency ? "$" : "") + value.ToString(pattern, CultureInfo.InvariantCulture) + (percent ? "%" : "");
    }
}
