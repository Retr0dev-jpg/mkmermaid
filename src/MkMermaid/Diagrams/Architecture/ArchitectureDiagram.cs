using System.Text.RegularExpressions;
using MkMermaid.Layout;
using MkMermaid.Parsing;
using MkMermaid.Rendering;
using MkMermaid.Scene;
using MkMermaid.Text;
using static MkMermaid.Parsing.ParseHelpers;

namespace MkMermaid.Diagrams.Architecture;

internal sealed class ArchNode(string id, bool junction, int order)
{
    public string Id { get; } = id;

    public bool IsJunction { get; } = junction;

    public int Order { get; } = order;

    public string? Icon { get; set; }

    public string? Label { get; set; }

    public string? Group { get; set; }

    public (int X, int Y)? Cell { get; set; }

    public Point Center { get; set; }
}

internal sealed class ArchGroup(string id, int order)
{
    public string Id { get; } = id;

    public int Order { get; } = order;

    public string? Icon { get; set; }

    public string? Label { get; set; }

    public string? Parent { get; set; }

    public Rect Bounds { get; set; }
}

internal sealed record ArchEdge(string From, char FromSide, string To, char ToSide, bool StartArrow, bool EndArrow, string? Label);

internal sealed class ArchitectureModel : IDiagramModel
{
    public string? Title { get; set; }

    public Dictionary<string, ArchNode> Nodes { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, ArchGroup> Groups { get; } = new(StringComparer.Ordinal);

    public List<ArchEdge> Edges { get; } = [];

    public DiagramScene Render(RenderContext context, DiagramInfo info) => ArchitectureRenderer.Render(this, context, info);
}

internal static partial class ArchitectureParser
{
    private const int MaxDepth = 32;

    [GeneratedRegex(@"^(?<kind>service|group)\s+(?<id>[\w-]+)\s*(?:\((?<icon>[^)]*)\))?\s*(?:\[(?<label>[^\]]*)\])?\s*(?:in\s+(?<parent>[\w-]+))?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Declaration();

    [GeneratedRegex(@"^junction\s+(?<id>[\w-]+)\s*(?:in\s+(?<parent>[\w-]+))?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Junction();

    [GeneratedRegex(@"^(?<a>[\w-]+)(?:\{group\})?\s*:\s*(?<as>[LRTB])\s*(?<start><)?-(?:\[(?<label>[^\]]*)\])?-(?<end>>)?\s*(?<bs>[LRTB])\s*:\s*(?<b>[\w-]+)(?:\{group\})?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Edge();

    public static ArchitectureModel Parse(Source source)
    {
        var model = new ArchitectureModel();
        foreach (var line in source.Lines)
        {
            var text = line.Trimmed;
            if (StartsWithWord(text, "title"))
            {
                model.Title = text[5..].Trim();
                continue;
            }

            CheckCount(model.Nodes.Count + model.Groups.Count + model.Edges.Count + 1, MermaidEngine.MaxElements, line.Number, "statements");
            if (Declaration().Match(text) is { Success: true } declaration)
            {
                var id = declaration.Groups["id"].Value;
                var icon = declaration.Groups["icon"].Success ? declaration.Groups["icon"].Value.Trim() : null;
                var label = declaration.Groups["label"].Success ? Unquote(declaration.Groups["label"].Value) : null;
                var parent = declaration.Groups["parent"].Success ? declaration.Groups["parent"].Value : null;
                if (model.Nodes.ContainsKey(id) || model.Groups.ContainsKey(id))
                {
                    throw new MermaidParseException(MermaidErrorCode.InvalidValue, $"'{Source.Truncate(id)}' is declared twice.", line.Number);
                }

                if (declaration.Groups["kind"].Value == "group")
                {
                    model.Groups[id] = new ArchGroup(id, model.Groups.Count) { Icon = icon, Label = label, Parent = parent };
                }
                else
                {
                    model.Nodes[id] = new ArchNode(id, false, model.Nodes.Count) { Icon = icon, Label = label, Group = parent };
                }

                continue;
            }

            if (Junction().Match(text) is { Success: true } junction)
            {
                var id = junction.Groups["id"].Value;
                model.Nodes[id] = new ArchNode(id, true, model.Nodes.Count) { Group = junction.Groups["parent"].Success ? junction.Groups["parent"].Value : null };
                continue;
            }

            if (Edge().Match(text) is { Success: true } edge)
            {
                model.Edges.Add(new ArchEdge(edge.Groups["a"].Value, edge.Groups["as"].Value[0], edge.Groups["b"].Value, edge.Groups["bs"].Value[0],
                    edge.Groups["start"].Success, edge.Groups["end"].Success, edge.Groups["label"].Success ? edge.Groups["label"].Value : null));
                continue;
            }

            throw Error(line, $"Unrecognized statement '{Source.Truncate(text)}'.");
        }

        foreach (var group in model.Groups.Values.Where(g => g.Parent is not null && !model.Groups.ContainsKey(g.Parent)))
        {
            throw new MermaidParseException(MermaidErrorCode.UnknownReference, $"Group '{Source.Truncate(group.Id)}' is placed in unknown group '{Source.Truncate(group.Parent!)}'.", source.HeaderLine);
        }

        foreach (var group in model.Groups.Values)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { group.Id };
            for (var g = group; g.Parent is not null; g = model.Groups[g.Parent])
            {
                if (!visited.Add(g.Parent))
                {
                    throw new MermaidParseException(MermaidErrorCode.InvalidValue, $"Group '{Source.Truncate(g.Parent)}' is placed inside itself.", source.HeaderLine);
                }

                if (visited.Count > MaxDepth + 1)
                {
                    throw new MermaidParseException(MermaidErrorCode.TooDeep, $"Groups are nested more than {MaxDepth} levels deep.", source.HeaderLine);
                }
            }
        }

        foreach (var node in model.Nodes.Values.Where(n => n.Group is not null && !model.Groups.ContainsKey(n.Group)))
        {
            throw new MermaidParseException(MermaidErrorCode.UnknownReference, $"'{Source.Truncate(node.Id)}' is placed in unknown group '{Source.Truncate(node.Group!)}'.", source.HeaderLine);
        }

        foreach (var edge in model.Edges)
        {
            foreach (var end in new[] { edge.From, edge.To })
            {
                if (!model.Nodes.ContainsKey(end))
                {
                    throw new MermaidParseException(MermaidErrorCode.UnknownReference, $"Edge refers to '{Source.Truncate(end)}', which is not a service or junction.", source.HeaderLine);
                }
            }
        }

        return model;
    }
}

/// <summary>
/// Grid placement from edge sides: <c>a:R -- L:b</c> puts b one cell to the right of a, mixed sides put b on the
/// diagonal. Occupied cells push the newcomer further along the same direction. Groups are fitted around members.
/// </summary>
internal static class ArchitectureRenderer
{
    private const double LabelGap = 4;

    public static DiagramScene Render(ArchitectureModel model, RenderContext context, DiagramInfo info)
    {
        var theme = context.Theme;
        var config = context.Config;
        var iconSize = config.Number("architecture", "iconSize", 80);
        var fontSize = config.Number("architecture", "fontSize", 16);
        var padding = config.Number("architecture", "padding", 40);
        var stepX = iconSize * 2.25;
        var stepY = iconSize * 2;

        Place(model);
        foreach (var node in model.Nodes.Values)
        {
            var (x, y) = node.Cell!.Value;
            node.Center = new Point(x * stepX, y * stepY);
        }

        var nodeLabels = model.Nodes.Values.Where(n => !n.IsJunction)
            .ToDictionary(n => n.Id, n => context.Measure(n.Label ?? n.Id, fontSize, iconSize * 1.8, forceWrap: true), StringComparer.Ordinal);
        double Below(ArchNode node) => nodeLabels.TryGetValue(node.Id, out var label) ? label.Height + LabelGap : 0;

        // Groups, innermost first, so a parent can wrap its child groups.
        var depth = model.Groups.Values.ToDictionary(g => g.Id, g => Depth(model, g), StringComparer.Ordinal);
        var labels = new Dictionary<string, MeasuredText>(StringComparer.Ordinal);
        foreach (var group in model.Groups.Values.OrderByDescending(g => depth[g.Id]))
        {
            var bounds = new Bounds();
            foreach (var node in model.Nodes.Values.Where(n => n.Group == group.Id))
            {
                var half = node.IsJunction ? 1 : iconSize / 2;
                bounds.Add(node.Center.X - half, node.Center.Y - half);
                bounds.Add(node.Center.X + half, node.Center.Y + half + Below(node));
            }

            foreach (var child in model.Groups.Values.Where(g => g.Parent == group.Id))
            {
                bounds.Add(child.Bounds.X, child.Bounds.Y);
                bounds.Add(child.Bounds.Right, child.Bounds.Bottom);
            }

            if (bounds.IsEmpty)
            {
                bounds.Add(0, 0);
            }

            var label = context.MeasurePlain(group.Label ?? group.Id, fontSize);
            labels[group.Id] = label;
            var top = label.Height + 12;
            group.Bounds = new Rect(bounds.MinX - padding / 2, bounds.MinY - padding / 2 - top, bounds.MaxX - bounds.MinX + padding, bounds.MaxY - bounds.MinY + padding + top);
        }

        var canvas = context.CreateCanvas();
        var groupBorder = theme.Hex("archGroupBorderColor", theme.Hex("lineColor"));
        var groupWidth = theme.Number("archGroupBorderWidth", 2);
        foreach (var group in model.Groups.Values.OrderBy(g => depth[g.Id]))
        {
            var r = group.Bounds;
            canvas.Rect(r.X, r.Y, r.Width, r.Height, null, groupBorder, groupWidth, 0, [8, 4]);
            var x = r.X + 8;
            if (group.Icon is not null)
            {
                Icon(canvas, group.Icon, new Rect(x, r.Y + 6, 24, 24));
                x += 30;
            }

            canvas.Label(labels[group.Id], x, r.Y + 6 + (24 - labels[group.Id].Height) / 2, TextAnchor.Start, theme.Hex("textColor"));
        }

        var edgeColor = theme.Hex("archEdgeColor", theme.Hex("lineColor"));
        var edgeWidth = theme.Number("archEdgeWidth", 3);
        var background = theme.Hex("background", "#FFFFFF");
        foreach (var edge in model.Edges)
        {
            var a = model.Nodes[edge.From];
            var b = model.Nodes[edge.To];
            var start = Port(a, edge.FromSide, iconSize, Below(a));
            var end = Port(b, edge.ToSide, iconSize, Below(b));
            var points = new List<Point> { start };
            var horizontalStart = edge.FromSide is 'L' or 'R';
            var horizontalEnd = edge.ToSide is 'L' or 'R';
            if (horizontalStart != horizontalEnd)
            {
                points.Add(horizontalStart ? new Point(end.X, start.Y) : new Point(start.X, end.Y));
            }
            else if (horizontalStart ? Math.Abs(start.Y - end.Y) > 0.5 : Math.Abs(start.X - end.X) > 0.5)
            {
                var mid = horizontalStart ? (start.X + end.X) / 2 : (start.Y + end.Y) / 2;
                points.Add(horizontalStart ? new Point(mid, start.Y) : new Point(start.X, mid));
                points.Add(horizontalStart ? new Point(mid, end.Y) : new Point(end.X, mid));
            }

            points.Add(end);
            var label = edge.Label is null ? null : context.Measure(edge.Label, fontSize * 0.8);
            var visual = new EdgeVisual(edgeColor, edgeWidth, null, edge.StartArrow ? Marker.Arrow : Marker.None, edge.EndArrow ? Marker.Arrow : Marker.None,
                CurveKind.Linear, label, theme.Hex("textColor"), background, MarkerScale: 1.2);
            EdgeDrawer.Draw(canvas, points, visual, null, background);
        }

        foreach (var node in model.Nodes.Values)
        {
            if (node.IsJunction)
            {
                continue;
            }

            var r = new Rect(node.Center.X - iconSize / 2, node.Center.Y - iconSize / 2, iconSize, iconSize);
            Icon(canvas, node.Icon, r);
            canvas.Label(nodeLabels[node.Id], node.Center.X, r.Bottom + LabelGap, TextAnchor.Middle, theme.Hex("textColor"));
        }

        canvas.Title(info.Title, theme.Hex("textColor"));
        return canvas.Finish(DiagramKind.Architecture, info.Title, info.AccDescription, 10, null);
    }

    private static int Depth(ArchitectureModel model, ArchGroup group)
    {
        var depth = 0;
        for (var g = group; g.Parent is not null; g = model.Groups[g.Parent])
        {
            depth++;
        }

        return depth;
    }

    private static (int X, int Y) Direction(char side) => side switch
    {
        'L' => (-1, 0),
        'R' => (1, 0),
        'T' => (0, -1),
        _ => (0, 1)
    };

    private static void Place(ArchitectureModel model)
    {
        var occupied = new HashSet<(int, int)>();
        var adjacency = model.Nodes.Keys.ToDictionary(k => k, _ => new List<(ArchEdge Edge, bool Forward)>(), StringComparer.Ordinal);
        foreach (var edge in model.Edges)
        {
            adjacency[edge.From].Add((edge, true));
            adjacency[edge.To].Add((edge, false));
        }

        var nextColumn = 0;
        foreach (var start in model.Nodes.Values.OrderBy(n => n.Order))
        {
            if (start.Cell is not null)
            {
                continue;
            }

            start.Cell = Free(occupied, (nextColumn, 0), (1, 0));
            occupied.Add(start.Cell.Value);
            var queue = new Queue<ArchNode>([start]);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                foreach (var (edge, forward) in adjacency[node.Id])
                {
                    var other = model.Nodes[forward ? edge.To : edge.From];
                    if (other.Cell is not null)
                    {
                        continue;
                    }

                    // Leaving through side s and entering the other through side t: step along s, then against t.
                    var (s, t) = forward ? (edge.FromSide, edge.ToSide) : (edge.ToSide, edge.FromSide);
                    var ds = Direction(s);
                    var dt = Direction(t);
                    var offset = (X: ds.X - (ds.X == -dt.X && ds.Y == -dt.Y ? 0 : dt.X), Y: ds.Y - (ds.X == -dt.X && ds.Y == -dt.Y ? 0 : dt.Y));
                    if (offset == (0, 0))
                    {
                        offset = ds;
                    }

                    var (cx, cy) = node.Cell!.Value;
                    other.Cell = Free(occupied, (cx + offset.X, cy + offset.Y), ds);
                    occupied.Add(other.Cell.Value);
                    queue.Enqueue(other);
                }
            }

            nextColumn = occupied.Max(c => c.Item1) + 1;
        }

        if (model.Nodes.Count == 0)
        {
            return;
        }

        // Shift to non-negative cells.
        var minX = model.Nodes.Values.Min(n => n.Cell!.Value.X);
        var minY = model.Nodes.Values.Min(n => n.Cell!.Value.Y);
        foreach (var node in model.Nodes.Values)
        {
            node.Cell = (node.Cell!.Value.X - minX, node.Cell.Value.Y - minY);
        }
    }

    private static (int X, int Y) Free(HashSet<(int, int)> occupied, (int X, int Y) cell, (int X, int Y) step)
    {
        for (var i = 0; i < 1000 && occupied.Contains(cell); i++)
        {
            cell = (cell.X + step.X, cell.Y + step.Y);
        }

        return cell;
    }

    /// <summary>Bottom ports start under the service label so the edge never crosses its text.</summary>
    private static Point Port(ArchNode node, char side, double iconSize, double below)
    {
        var half = node.IsJunction ? 0 : iconSize / 2;
        var (dx, dy) = Direction(side);
        return new Point(node.Center.X + dx * half, node.Center.Y + dy * half + (side == 'B' ? below : 0));
    }

    /// <summary>The built-in icons (cloud, database, disk, internet, server) as white glyphs on Mermaid's blue tile.</summary>
    private static void Icon(Canvas canvas, string? name, Rect r)
    {
        const string tile = "#087EBF";
        const string glyph = "#FFFFFF";
        canvas.Rect(r.X, r.Y, r.Width, r.Height, tile, null, 0);
        var (cx, cy, s) = (r.CenterX, r.CenterY, r.Width / 80);
        switch (name)
        {
            case "cloud":
                var cloud = new PathGeometry().MoveTo(cx - 24 * s, cy + 14 * s);
                cloud.CubicTo(cx - 36 * s, cy + 14 * s, cx - 36 * s, cy - 4 * s, cx - 22 * s, cy - 4 * s);
                cloud.CubicTo(cx - 20 * s, cy - 20 * s, cx + 2 * s, cy - 24 * s, cx + 8 * s, cy - 10 * s);
                cloud.CubicTo(cx + 22 * s, cy - 16 * s, cx + 34 * s, cy - 4 * s, cx + 28 * s, cy + 6 * s);
                cloud.CubicTo(cx + 36 * s, cy + 8 * s, cx + 34 * s, cy + 14 * s, cx + 26 * s, cy + 14 * s).Close();
                canvas.Shape(cloud, glyph, null, 0);
                break;
            case "database":
                Shapes.Draw(canvas, NodeShape.Cylinder, new Rect(cx - 20 * s, cy - 26 * s, 40 * s, 52 * s), tile, glyph, 3 * s);
                canvas.Shape(new PathGeometry().MoveTo(cx - 20 * s, cy - 4 * s).CubicTo(cx - 20 * s, cy + 6 * s, cx + 20 * s, cy + 6 * s, cx + 20 * s, cy - 4 * s), null, glyph, 3 * s);
                canvas.Shape(new PathGeometry().MoveTo(cx - 20 * s, cy + 8 * s).CubicTo(cx - 20 * s, cy + 18 * s, cx + 20 * s, cy + 18 * s, cx + 20 * s, cy + 8 * s), null, glyph, 3 * s);
                break;
            case "disk":
                canvas.Rect(cx - 22 * s, cy - 28 * s, 44 * s, 56 * s, null, glyph, 3 * s, 4 * s);
                canvas.Circle(cx, cy - 4 * s, 14 * s, null, glyph, 3 * s);
                canvas.Circle(cx, cy - 4 * s, 3 * s, glyph, null, 0);
                canvas.Line(cx + 2 * s, cy - 2 * s, cx - 10 * s, cy + 18 * s, glyph, 3 * s);
                break;
            case "internet":
                canvas.Circle(cx, cy, 26 * s, null, glyph, 3 * s);
                canvas.Shape(PathGeometry.Ellipse(cx, cy, 11 * s, 26 * s), null, glyph, 3 * s);
                canvas.Line(cx - 26 * s, cy, cx + 26 * s, cy, glyph, 3 * s);
                canvas.Line(cx, cy - 26 * s, cx, cy + 26 * s, glyph, 3 * s);
                canvas.Shape(new PathGeometry().MoveTo(cx - 22 * s, cy - 13 * s).CubicTo(cx - 8 * s, cy - 8 * s, cx + 8 * s, cy - 8 * s, cx + 22 * s, cy - 13 * s), null, glyph, 2 * s);
                canvas.Shape(new PathGeometry().MoveTo(cx - 22 * s, cy + 13 * s).CubicTo(cx - 8 * s, cy + 8 * s, cx + 8 * s, cy + 8 * s, cx + 22 * s, cy + 13 * s), null, glyph, 2 * s);
                break;
            case "server":
                for (var i = 0; i < 3; i++)
                {
                    var y = cy - 26 * s + i * 18 * s;
                    canvas.Rect(cx - 24 * s, y, 48 * s, 14 * s, null, glyph, 3 * s, 2 * s);
                    canvas.Circle(cx + 15 * s, y + 7 * s, 2.5 * s, glyph, null, 0);
                }

                break;
            default:
                canvas.Text("?", cx, cy + 14 * s, 40 * s, TextAnchor.Middle, glyph, bold: true);
                break;
        }
    }
}
