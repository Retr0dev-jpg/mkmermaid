using System.Text.RegularExpressions;
using MkMermaid.Diagrams.Flowchart;
using MkMermaid.Layout;
using MkMermaid.Parsing;
using MkMermaid.Rendering;
using MkMermaid.Scene;
using MkMermaid.Text;
using static MkMermaid.Parsing.ParseHelpers;

namespace MkMermaid.Diagrams.Block;

internal enum BlockKind
{
    Node,
    Space,
    Composite,
    Arrow
}

internal sealed class BlockItem(string id, BlockKind kind)
{
    public string Id { get; } = id;

    public BlockKind Kind { get; } = kind;

    public string? Label { get; set; }

    public NodeShape Shape { get; set; } = NodeShape.Rect;

    /// <summary>For block arrows: right, left, up, down, x (horizontal both ways) or y (vertical both ways).</summary>
    public string Direction { get; set; } = "right";

    public int Span { get; set; } = 1;

    /// <summary>Columns of a composite block; null puts every child on one row.</summary>
    public int? Columns { get; set; }

    public List<BlockItem> Children { get; } = [];

    public List<string> Classes { get; } = [];

    public Style Style { get; set; } = Style.None;

    public double Width { get; set; }

    public double Height { get; set; }

    public double X { get; set; }

    public double Y { get; set; }
}

internal sealed record BlockEdge(string From, string To, string? Label, Marker Start, Marker End, IReadOnlyList<double>? Dash, double Width);

internal sealed class BlockModel : IDiagramModel
{
    public string? Title => null;

    public BlockItem Root { get; } = new("", BlockKind.Composite);

    public Dictionary<string, BlockItem> Items { get; } = new(StringComparer.Ordinal);

    public List<BlockEdge> Edges { get; } = [];

    public Dictionary<string, Style> ClassDefs { get; } = new(StringComparer.Ordinal);

    public DiagramScene Render(RenderContext context, DiagramInfo info) => BlockRenderer.Render(this, context, info);
}

internal static partial class BlockParser
{
    private const int MaxDepth = 32;

    [GeneratedRegex(@"^\s*(?<s>[<xo])?(?:(?:--|==|-\.)\s*""(?<t>[^""]*)""\s*(?<l>-{2,}|={2,}|\.+-)(?<e>[>xo])?|(?<l>-{2,}|={2,}|-\.+-)(?<e>[>xo])?(?:\s*\|(?<t>[^|]*)\|)?)", RegexOptions.CultureInvariant)]
    private static partial Regex Link();

    [GeneratedRegex(@"^[\p{L}\p{N}_][\p{L}\p{N}_-]*", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();

    public static BlockModel Parse(Source source)
    {
        var model = new BlockModel();
        var stack = new Stack<BlockItem>();
        stack.Push(model.Root);
        var anonymous = 0;
        foreach (var line in source.Lines)
        {
            var text = line.Trimmed;
            if (StartsWithWord(text, "classDef"))
            {
                var parts = text[8..].Trim().Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    foreach (var id in parts[0].Split(','))
                    {
                        model.ClassDefs[id.Trim()] = Style.Parse(parts[1]);
                    }
                }

                continue;
            }

            if (StartsWithWord(text, "class"))
            {
                var parts = text[5..].Trim().Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    foreach (var id in parts[0].Split(','))
                    {
                        if (model.Items.TryGetValue(id.Trim(), out var item))
                        {
                            item.Classes.AddRange(parts[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                        }
                    }
                }

                continue;
            }

            if (StartsWithWord(text, "style"))
            {
                var parts = text[5..].Trim().Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    foreach (var id in parts[0].Split(','))
                    {
                        if (model.Items.TryGetValue(id.Trim(), out var item))
                        {
                            item.Style = item.Style.Merge(Style.Parse(parts[1]));
                        }
                    }
                }

                continue;
            }

            var pos = 0;
            string? previous = null;
            BlockEdge? pending = null;
            while (true)
            {
                while (pos < text.Length && char.IsWhiteSpace(text[pos]))
                {
                    pos++;
                }

                if (pos >= text.Length)
                {
                    break;
                }

                var rest = text[pos..];
                if (previous is not null && Link().Match(rest) is { Success: true } link)
                {
                    var head = link.Groups["e"].Value;
                    var tail = link.Groups["s"].Value;
                    var lineKind = link.Groups["l"].Value;
                    pending = new BlockEdge(previous, "", link.Groups["t"].Success ? link.Groups["t"].Value : null,
                        MarkerFor(tail), MarkerFor(head), lineKind.Contains('.') ? [3, 3] : null, lineKind.StartsWith('=') ? 3 : 1);
                    pos += link.Length;
                    continue;
                }

                if (StartsWithWord(rest, "columns"))
                {
                    var value = rest[7..].TrimStart();
                    var token = value.Split(' ', 2)[0];
                    stack.Peek().Columns = token == "auto" ? null : int.TryParse(token, out var columns) && columns > 0 ? Math.Min(columns, 100) : throw Error(line, "columns takes a positive number or auto.");
                    pos = text.Length - value.Length + token.Length;
                    continue;
                }

                if (StartsWithWord(rest, "end"))
                {
                    if (stack.Count == 1)
                    {
                        throw Error(line, "'end' without a matching block.");
                    }

                    stack.Pop();
                    pos += 3;
                    continue;
                }

                if (StartsWithWord(rest, "space"))
                {
                    pos += 5;
                    var space = new BlockItem($"space{++anonymous}", BlockKind.Space) { Span = ReadSpan(text, ref pos) };
                    stack.Peek().Children.Add(space);
                    continue;
                }

                if (StartsWithWord(rest, "block"))
                {
                    pos += 5;
                    var id = $"block{++anonymous}";
                    if (pos < text.Length && text[pos] == ':' && Identifier().Match(text[(pos + 1)..]) is { Success: true } named)
                    {
                        id = named.Value;
                        pos += 1 + named.Length;
                    }

                    var composite = new BlockItem(id, BlockKind.Composite);
                    if (FlowchartParser.ReadShapeAt(text, pos, out var compositeLabel, out _, out var afterLabel, out _))
                    {
                        composite.Label = compositeLabel;
                        pos = afterLabel;
                    }

                    composite.Span = ReadSpan(text, ref pos);
                    CheckCount(model.Items.Count + 1, MermaidEngine.MaxElements, line.Number, "blocks");
                    if (stack.Count > MaxDepth)
                    {
                        throw new MermaidParseException(MermaidErrorCode.TooDeep, $"Blocks are nested more than {MaxDepth} levels deep.", line.Number);
                    }

                    model.Items[id] = composite;
                    stack.Peek().Children.Add(composite);
                    stack.Push(composite);
                    continue;
                }

                var identifier = Identifier().Match(rest);
                if (!identifier.Success)
                {
                    throw Error(line, $"Unexpected '{Source.Truncate(rest)}'.");
                }

                var itemId = identifier.Value;
                pos += identifier.Length;
                var isNew = !model.Items.TryGetValue(itemId, out var node);
                node ??= new BlockItem(itemId, BlockKind.Node);
                if (pos + 1 < text.Length && text[pos] == '<' && text[pos + 1] == '[')
                {
                    var close = text.IndexOf("]>", pos, StringComparison.Ordinal);
                    if (close < 0)
                    {
                        throw Error(line, "A block arrow is written id<[\"label\"]>(direction).");
                    }

                    node = new BlockItem(itemId, BlockKind.Arrow) { Label = Unquote(text[(pos + 2)..close]) };
                    pos = close + 2;
                    if (pos < text.Length && text[pos] == '(')
                    {
                        var end = text.IndexOf(')', pos);
                        if (end > pos)
                        {
                            node.Direction = text[(pos + 1)..end].Trim().ToLowerInvariant();
                            pos = end + 1;
                        }
                    }

                    isNew = true;
                }
                else if (FlowchartParser.ReadShapeAt(text, pos, out var label, out var shape, out var next, out var error))
                {
                    if (error is not null)
                    {
                        throw Error(line, error);
                    }

                    node.Label = label;
                    node.Shape = shape;
                    pos = next;
                }

                var span = ReadSpan(text, ref pos);
                if (span > 1)
                {
                    node.Span = span;
                }

                if (isNew)
                {
                    CheckCount(model.Items.Count + 1, MermaidEngine.MaxElements, line.Number, "blocks");
                    model.Items[itemId] = node;

                    // A node that only appears in an edge still occupies a cell, as in Mermaid.
                    stack.Peek().Children.Add(node);
                }

                if (pending is not null)
                {
                    CheckCount(model.Edges.Count + 1, MermaidEngine.MaxElements, line.Number, "edges");
                    model.Edges.Add(pending with { To = itemId });
                    pending = null;
                }

                previous = itemId;
            }

            if (pending is not null)
            {
                throw Error(line, "An edge needs a target block.");
            }
        }

        if (stack.Count > 1)
        {
            throw Error(source.Lines.Count == 0 ? source.HeaderLine : source.Lines[^1].Number, $"Block '{Source.Truncate(stack.Peek().Id)}' is missing 'end'.");
        }

        return model;
    }

    private static int ReadSpan(string text, ref int pos)
    {
        if (pos < text.Length && text[pos] == ':')
        {
            var start = pos + 1;
            var end = start;
            while (end < text.Length && char.IsDigit(text[end]))
            {
                end++;
            }

            if (end > start && int.TryParse(text[start..end], out var span) && span > 0)
            {
                pos = end;
                return Math.Min(span, 100);
            }
        }

        return 1;
    }

    private static Marker MarkerFor(string symbol) => symbol switch
    {
        ">" or "<" => Marker.Arrow,
        "x" => Marker.Cross,
        "o" => Marker.Circle,
        _ => Marker.None
    };
}

internal static class BlockRenderer
{
    private const double Padding = 8;

    public static DiagramScene Render(BlockModel model, RenderContext context, DiagramInfo info)
    {
        var theme = context.Theme;
        var labels = new Dictionary<string, MeasuredText>(StringComparer.Ordinal);
        Size(model.Root, context, labels);
        Arrange(model.Root, 0, 0, model.Root.Width, model.Root.Height, labels);

        var canvas = context.CreateCanvas();
        DrawItem(model.Root, canvas, context, model, labels, isRoot: true);

        var background = theme.Hex("background", "#FFFFFF");
        foreach (var edge in model.Edges)
        {
            if (!model.Items.TryGetValue(edge.From, out var from) || !model.Items.TryGetValue(edge.To, out var to))
            {
                continue;
            }

            var a = Bounds(from);
            var b = Bounds(to);
            var points = EdgeDrawer.Clip([new Point(a.CenterX, a.CenterY), new Point(b.CenterX, b.CenterY)], Shapes.Outline(from.Shape, a), Shapes.Outline(to.Shape, b));
            var label = edge.Label is null ? null : context.Measure(edge.Label);
            var visual = new EdgeVisual(theme.Hex("lineColor"), edge.Width, edge.Dash, edge.Start, edge.End, CurveKind.Linear, label,
                theme.Hex("textColor"), theme.Hex("edgeLabelBackground"));
            EdgeDrawer.Draw(canvas, points, visual, null, background);
        }

        canvas.Title(info.Title, theme.Hex("textColor"));
        return canvas.Finish(DiagramKind.Block, info.Title, info.AccDescription, 8, null);
    }

    private static Rect Bounds(BlockItem item) => new(item.X, item.Y, item.Width, item.Height);

    /// <summary>
    /// Natural sizes bottom-up. Every child of a container gets the widest child's unit width (times its span) and
    /// the tallest child's height, which gives Mermaid's even grid.
    /// </summary>
    private static void Size(BlockItem item, RenderContext context, Dictionary<string, MeasuredText> labels)
    {
        if (item.Kind != BlockKind.Composite)
        {
            if (item.Kind == BlockKind.Space)
            {
                (item.Width, item.Height) = (0, 0);
                return;
            }

            var label = context.Measure(item.Label ?? item.Id);
            labels[item.Id] = label;
            if (item.Kind == BlockKind.Arrow)
            {
                (item.Width, item.Height) = item.Direction is "up" or "down" or "y"
                    ? (label.Width + 4 * Padding, label.Height + 8 * Padding)
                    : (label.Width + 8 * Padding, label.Height + 4 * Padding);
                return;
            }

            (item.Width, item.Height) = Shapes.Size(item.Shape, label.Width, label.Height, Padding);
            return;
        }

        foreach (var child in item.Children)
        {
            Size(child, context, labels);
        }

        if (item.Label is not null)
        {
            labels[item.Id] = context.Measure(item.Label);
        }

        var (unit, rowHeight, columns, rows) = Grid(item);
        var labelHeight = labels.TryGetValue(item.Id, out var own) ? own.Height + Padding : 0;
        item.Width = Math.Max(columns * (unit + Padding) + Padding, own is null ? 0 : own.Width + 2 * Padding);
        item.Height = rows * (rowHeight + Padding) + Padding + labelHeight;
    }

    /// <summary>Unit column width, row height, column count and row count of a composite's natural grid.</summary>
    private static (double Unit, double RowHeight, int Columns, int Rows) Grid(BlockItem item)
    {
        if (item.Children.Count == 0)
        {
            return (0, 0, 0, 0);
        }

        var unit = item.Children.Max(c => (c.Width - Padding * (c.Span - 1)) / c.Span);
        var rowHeight = item.Children.Max(c => c.Height);
        var cells = item.Children.Sum(c => c.Span);
        var columns = Math.Min(item.Columns ?? cells, cells);
        var (_, rows) = Cells(item, item.Columns ?? cells);
        return (unit, rowHeight, Math.Max(columns, 1), rows);
    }

    /// <summary>Grid cell of every child, row-major; a block that does not fit in the rest of a row starts the next one.</summary>
    private static (List<(int Column, int Row)> Cells, int Rows) Cells(BlockItem item, int columns)
    {
        var result = new List<(int, int)>();
        var cell = 0;
        foreach (var child in item.Children)
        {
            var column = cell % columns;
            if (column > 0 && column + child.Span > columns)
            {
                cell += columns - column;
                column = 0;
            }

            result.Add((column, cell / columns));
            cell += child.Span;
        }

        return (result, (int)Math.Ceiling(cell / (double)columns));
    }

    /// <summary>
    /// Places <paramref name="item"/> in the given box and lays its children on the grid; a composite stretched
    /// past its natural width widens its columns, as Mermaid does.
    /// </summary>
    private static void Arrange(BlockItem item, double x, double y, double width, double height, Dictionary<string, MeasuredText> labels)
    {
        (item.X, item.Y, item.Width, item.Height) = (x, y, width, height);
        if (item.Kind != BlockKind.Composite || item.Children.Count == 0)
        {
            return;
        }

        var (_, rowHeight, columns, rows) = Grid(item);
        var labelHeight = labels.TryGetValue(item.Id, out var own) ? own.Height + Padding : 0;
        var unit = (width - Padding * (columns + 1)) / columns;
        var content = rows * (rowHeight + Padding) + Padding;
        var top = y + labelHeight + Math.Max(0, (height - labelHeight - content) / 2);
        var (cells, _) = Cells(item, item.Columns ?? item.Children.Sum(c => c.Span));
        for (var i = 0; i < item.Children.Count; i++)
        {
            var child = item.Children[i];
            var (column, row) = cells[i];
            Arrange(child, x + Padding + column * (unit + Padding), top + Padding + row * (rowHeight + Padding),
                unit * child.Span + Padding * (child.Span - 1), rowHeight, labels);
        }
    }

    private static void DrawItem(BlockItem item, Canvas canvas, RenderContext context, BlockModel model, Dictionary<string, MeasuredText> labels, bool isRoot)
    {
        var theme = context.Theme;
        var style = item.Classes.Count == 0 ? model.ClassDefs.GetValueOrDefault("default") ?? Style.None : Style.None;
        foreach (var name in item.Classes)
        {
            style = style.Merge(model.ClassDefs.GetValueOrDefault(name));
        }

        style = style.Merge(item.Style);
        var r = Bounds(item);
        var color = style.Color ?? theme.Hex("nodeTextColor", theme.Hex("textColor"));
        switch (item.Kind)
        {
            case BlockKind.Composite when !isRoot:
                canvas.Rect(r.X, r.Y, r.Width, r.Height, Style.Resolve(style.Fill, theme.Hex("clusterBkg")), Style.Resolve(style.Stroke, theme.Hex("clusterBorder")),
                    style.StrokeWidth ?? 1, 5, style.Dash);
                if (labels.TryGetValue(item.Id, out var title))
                {
                    canvas.Label(title, r.CenterX, r.Y + Padding / 2, TextAnchor.Middle, theme.Hex("titleColor", color));
                }

                break;
            case BlockKind.Node:
                Shapes.Draw(canvas, item.Shape, r, Style.Resolve(style.Fill, theme.Hex("mainBkg")) ?? "#00000000",
                    Style.Resolve(style.Stroke, theme.Hex("nodeBorder")) ?? "#00000000", style.StrokeWidth ?? 1, style.Dash);
                canvas.LabelCentered(labels[item.Id], r.CenterX, r.CenterY, color);
                break;
            case BlockKind.Arrow:
                canvas.Shape(Arrow(r, item.Direction), Style.Resolve(style.Fill, theme.Hex("mainBkg")), Style.Resolve(style.Stroke, theme.Hex("nodeBorder")), style.StrokeWidth ?? 1);
                canvas.LabelCentered(labels[item.Id], r.CenterX, r.CenterY, color);
                break;
        }

        foreach (var child in item.Children)
        {
            DrawItem(child, canvas, context, model, labels, isRoot: false);
        }
    }

    private static PathGeometry Arrow(Rect r, string direction)
    {
        var head = Math.Min(r.Width, r.Height) / 2;
        var (x0, x1, y0, y1, cx, cy) = (r.X, r.Right, r.Y, r.Bottom, r.CenterX, r.CenterY);
        var shaftX = r.Height / 4;
        var shaftY = r.Width / 4;
        return direction switch
        {
            "left" => PathGeometry.Polygon([(x0, cy), (x0 + head, y0), (x0 + head, cy - shaftX), (x1, cy - shaftX), (x1, cy + shaftX), (x0 + head, cy + shaftX), (x0 + head, y1)]),
            "up" => PathGeometry.Polygon([(cx, y0), (x1, y0 + head), (cx + shaftY, y0 + head), (cx + shaftY, y1), (cx - shaftY, y1), (cx - shaftY, y0 + head), (x0, y0 + head)]),
            "down" => PathGeometry.Polygon([(cx, y1), (x0, y1 - head), (cx - shaftY, y1 - head), (cx - shaftY, y0), (cx + shaftY, y0), (cx + shaftY, y1 - head), (x1, y1 - head)]),
            "x" => PathGeometry.Polygon([(x0, cy), (x0 + head, y0), (x0 + head, cy - shaftX), (x1 - head, cy - shaftX), (x1 - head, y0), (x1, cy), (x1 - head, y1), (x1 - head, cy + shaftX), (x0 + head, cy + shaftX), (x0 + head, y1)]),
            "y" => PathGeometry.Polygon([(cx, y0), (x1, y0 + head), (cx + shaftY, y0 + head), (cx + shaftY, y1 - head), (x1, y1 - head), (cx, y1), (x0, y1 - head), (cx - shaftY, y1 - head), (cx - shaftY, y0 + head), (x0, y0 + head)]),
            _ => PathGeometry.Polygon([(x1, cy), (x1 - head, y0), (x1 - head, cy - shaftX), (x0, cy - shaftX), (x0, cy + shaftX), (x1 - head, cy + shaftX), (x1 - head, y1)])
        };
    }
}
