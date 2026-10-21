using MkMermaid.Parsing;
using MkMermaid.Rendering;
using MkMermaid.Scene;
using MkMermaid.Text;
using static MkMermaid.Parsing.ParseHelpers;

namespace MkMermaid.Diagrams.Mindmap;

internal enum MindmapShape
{
    Default,
    Square,
    Rounded,
    Circle,
    Bang,
    Cloud,
    Hexagon
}

internal sealed class MindmapNode(string text, MindmapShape shape, int indent)
{
    public string Text { get; } = text;

    public MindmapShape Shape { get; } = shape;

    public int Indent { get; } = indent;

    public List<MindmapNode> Children { get; } = [];

    public List<string> Classes { get; } = [];

    public string? Icon { get; set; }
}

internal sealed class MindmapModel : IDiagramModel
{
    public string? Title => null;

    public MindmapNode? Root { get; set; }

    public DiagramScene Render(RenderContext context, DiagramInfo info) => MindmapRenderer.Render(this, context, info);
}

internal static class MindmapParser
{
    private static readonly (string Open, string Close, MindmapShape Shape)[] Delimiters =
    [
        ("((", "))", MindmapShape.Circle),
        ("))", "((", MindmapShape.Bang),
        ("{{", "}}", MindmapShape.Hexagon),
        ("[", "]", MindmapShape.Square),
        ("(", ")", MindmapShape.Rounded),
        (")", "(", MindmapShape.Cloud)
    ];

    public static MindmapModel Parse(Source source)
    {
        var model = new MindmapModel();
        var stack = new List<MindmapNode>();
        MindmapNode? last = null;
        var count = 0;
        foreach (var line in source.Lines)
        {
            var text = line.Trimmed;
            if (text.StartsWith("::icon(", StringComparison.Ordinal) && text.EndsWith(')'))
            {
                if (last is not null)
                {
                    last.Icon = text[7..^1].Trim();
                }

                continue;
            }

            if (text.StartsWith(":::", StringComparison.Ordinal))
            {
                last?.Classes.AddRange(text[3..].Split(' ', StringSplitOptions.RemoveEmptyEntries));
                continue;
            }

            CheckCount(++count, MermaidEngine.MaxElements, line.Number, "nodes");
            var node = ParseNode(text, line.Indent, line);
            while (stack.Count > 0 && stack[^1].Indent >= node.Indent)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            if (stack.Count == 0)
            {
                if (model.Root is not null)
                {
                    throw Error(line, $"There can be only one root; indent '{Source.Truncate(text)}' under the root node.");
                }

                model.Root = node;
            }
            else
            {
                stack[^1].Children.Add(node);
            }

            stack.Add(node);
            last = node;
        }

        return model;
    }

    private static MindmapNode ParseNode(string text, int indent, SourceLine line)
    {
        var classes = new List<string>();
        var marker = text.IndexOf(":::", StringComparison.Ordinal);
        if (marker > 0)
        {
            classes.AddRange(text[(marker + 3)..].Split(' ', StringSplitOptions.RemoveEmptyEntries));
            text = text[..marker].TrimEnd();
        }

        foreach (var (open, close, shape) in Delimiters)
        {
            var start = text.IndexOf(open, StringComparison.Ordinal);
            if (start < 0 || !text.EndsWith(close, StringComparison.Ordinal) || text.Length < start + open.Length + close.Length)
            {
                continue;
            }

            // The id before the delimiter must be a plain word, otherwise the brackets belong to the text.
            if (start > 0 && !text[..start].All(c => IsIdChar(c) || c == '-'))
            {
                continue;
            }

            var body = text[(start + open.Length)..^close.Length];
            var node = new MindmapNode(Unquote(body), shape, indent);
            node.Classes.AddRange(classes);
            return node;
        }

        if (text.Length == 0)
        {
            throw Error(line, "Empty node.");
        }

        var plain = new MindmapNode(text, MindmapShape.Default, indent);
        plain.Classes.AddRange(classes);
        return plain;
    }
}

/// <summary>
/// Two-sided tidy tree: the root's branches are split between the right and the left so both halves have
/// similar height; each subtree is stacked vertically and placed one level further out.
/// </summary>
internal static class MindmapRenderer
{
    private const double LevelGap = 60;
    private const double SiblingGap = 16;
    private const double MaxLabelWidth = 200;

    private sealed class Box(MindmapNode node, MeasuredText label, double width, double height, int depth, int section)
    {
        public MindmapNode Node { get; } = node;

        public MeasuredText Label { get; } = label;

        public double Width { get; } = width;

        public double Height { get; } = height;

        public int Depth { get; } = depth;

        public int Section { get; } = section;

        public List<Box> Children { get; } = [];

        public double SubtreeHeight { get; set; }

        public double X { get; set; }

        public double Y { get; set; }
    }

    public static DiagramScene Render(MindmapModel model, RenderContext context, DiagramInfo info)
    {
        var theme = context.Theme;
        var canvas = context.CreateCanvas();
        if (model.Root is null)
        {
            return canvas.Finish(DiagramKind.Mindmap, info.Title, info.AccDescription, 10, null);
        }

        var padding = context.Config.Number("mindmap", "padding", 10);
        var root = Build(model.Root, context, 0, -1, padding);
        Measure(root);

        // Split branches so the two sides carry about the same height; the first half goes right.
        var total = root.Children.Sum(c => c.SubtreeHeight + SiblingGap);
        var right = new List<Box>();
        var left = new List<Box>();
        var acc = 0.0;
        foreach (var child in root.Children)
        {
            (acc < total / 2 || right.Count == 0 ? right : left).Add(child);
            acc += child.SubtreeHeight + SiblingGap;
        }

        root.X = -root.Width / 2;
        root.Y = -root.Height / 2;
        PlaceSide(right, root, 1);
        PlaceSide(left, root, -1);

        var edges = context.CreateCanvas();
        var nodes = context.CreateCanvas();
        Draw(root, edges, nodes, theme);
        canvas.Append(edges);
        canvas.Append(nodes);
        canvas.Title(info.Title, theme.Hex("textColor"));
        return canvas.Finish(DiagramKind.Mindmap, info.Title, info.AccDescription, 10, null);
    }

    private static Box Build(MindmapNode node, RenderContext context, int depth, int section, double padding)
    {
        var fontSize = depth == 0 ? context.FontSize * 1.2 : context.FontSize;
        var label = context.Measure(node.Text, fontSize, context.Config.Number("mindmap", "maxNodeWidth", MaxLabelWidth), forceWrap: true);
        var (w, h) = node.Shape switch
        {
            MindmapShape.Circle => (Math.Max(label.Width, label.Height) + 4 * padding, Math.Max(label.Width, label.Height) + 4 * padding),
            MindmapShape.Bang or MindmapShape.Cloud => (label.Width + 6 * padding, label.Height + 4 * padding),
            MindmapShape.Hexagon => (label.Width + 4 * padding, label.Height + 2 * padding),
            _ => (label.Width + 4 * padding, label.Height + 2 * padding)
        };
        var box = new Box(node, label, w, h, depth, section);
        for (var i = 0; i < node.Children.Count; i++)
        {
            box.Children.Add(Build(node.Children[i], context, depth + 1, depth == 0 ? i : section, padding));
        }

        return box;
    }

    private static void Measure(Box box)
    {
        foreach (var child in box.Children)
        {
            Measure(child);
        }

        var children = box.Children.Count == 0 ? 0 : box.Children.Sum(c => c.SubtreeHeight) + SiblingGap * (box.Children.Count - 1);
        box.SubtreeHeight = Math.Max(box.Height, children);
    }

    private static void PlaceSide(List<Box> branches, Box root, int direction)
    {
        var height = branches.Sum(b => b.SubtreeHeight) + SiblingGap * Math.Max(0, branches.Count - 1);
        var top = -height / 2;
        foreach (var branch in branches)
        {
            Place(branch, root, direction, top);
            top += branch.SubtreeHeight + SiblingGap;
        }
    }

    private static void Place(Box box, Box parent, int direction, double top)
    {
        box.X = direction > 0 ? parent.X + parent.Width + LevelGap : parent.X - LevelGap - box.Width;
        box.Y = top + (box.SubtreeHeight - box.Height) / 2;
        var children = box.Children.Sum(c => c.SubtreeHeight) + SiblingGap * Math.Max(0, box.Children.Count - 1);
        var childTop = top + (box.SubtreeHeight - children) / 2;
        foreach (var child in box.Children)
        {
            Place(child, box, direction, childTop);
            childTop += child.SubtreeHeight + SiblingGap;
        }
    }

    private static void Draw(Box box, Canvas edges, Canvas nodes, Theming.Theme theme)
    {
        var color = box.Section < 0 ? null : (box.Section % 11 + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var fill = color is null ? theme.Hex("git0") : theme.Hex("cScale" + color);
        var text = color is null ? theme.Hex("gitBranchLabel0") : theme.Hex("cScaleLabel" + color);
        foreach (var child in box.Children)
        {
            var toRight = child.X > box.X;
            var (x1, y1) = (toRight ? box.X + box.Width : box.X, box.Y + box.Height / 2);
            if (box.Depth == 0)
            {
                (x1, y1) = (box.X + box.Width / 2, box.Y + box.Height / 2);
            }

            var (x2, y2) = (toRight ? child.X : child.X + child.Width, child.Y + child.Height / 2);
            var mid = (x1 + x2) / 2;
            var path = new PathGeometry().MoveTo(x1, y1).CubicTo(mid, y1, mid, y2, x2, y2);
            edges.Shape(path, null, theme.Hex("cScale" + (child.Section % 11 + 1)), Math.Max(1, 14 - 3 * box.Depth));
            Draw(child, edges, nodes, theme);
        }

        var r = new Layout.Rect(box.X, box.Y, box.Width, box.Height);
        switch (box.Node.Shape)
        {
            case MindmapShape.Square:
                nodes.Rect(r.X, r.Y, r.Width, r.Height, fill, null, 0);
                break;
            case MindmapShape.Rounded:
                nodes.Rect(r.X, r.Y, r.Width, r.Height, fill, null, 0, Math.Min(r.Height / 2, 10));
                break;
            case MindmapShape.Circle:
                nodes.Circle(r.CenterX, r.CenterY, r.Width / 2, fill, null, 0);
                break;
            case MindmapShape.Hexagon:
                var inset = r.Height / 4;
                nodes.Shape(PathGeometry.Polygon([(r.X + inset, r.Y), (r.Right - inset, r.Y), (r.Right, r.CenterY), (r.Right - inset, r.Bottom), (r.X + inset, r.Bottom), (r.X, r.CenterY)]), fill, null, 0);
                break;
            case MindmapShape.Cloud:
                nodes.Shape(Cloud(r), fill, null, 0);
                break;
            case MindmapShape.Bang:
                nodes.Shape(Bang(r), fill, null, 0);
                break;
            default:
                var path = new PathGeometry().MoveTo(r.X, r.Bottom).LineTo(r.X, r.Y + 5);
                path.ArcTo(r.X + 5, r.Y + 5, 5, 5, Math.PI, Math.PI / 2).LineTo(r.Right - 5, r.Y);
                path.ArcTo(r.Right - 5, r.Y + 5, 5, 5, -Math.PI / 2, Math.PI / 2).LineTo(r.Right, r.Bottom).Close();
                nodes.Shape(path, fill, null, 0);
                nodes.Line(r.X, r.Bottom, r.Right, r.Bottom, color is null ? theme.Hex("gitInv0") : theme.Hex("cScaleInv" + color), 3);
                break;
        }

        nodes.Label(box.Label, r.CenterX, r.CenterY - box.Label.Height / 2, TextAnchor.Middle, text);
    }

    /// <summary>Bumpy outline made of arcs around the rectangle, like Mermaid's cloud node.</summary>
    private static PathGeometry Cloud(Layout.Rect r)
    {
        var points = new List<(double X, double Y)>();
        var bumpsX = Math.Max(2, (int)(r.Width / 40));
        var bumpsY = Math.Max(1, (int)(r.Height / 30));
        for (var i = 0; i <= bumpsX; i++)
        {
            points.Add((r.X + r.Width * i / bumpsX, r.Y));
        }

        for (var i = 1; i <= bumpsY; i++)
        {
            points.Add((r.Right, r.Y + r.Height * i / bumpsY));
        }

        for (var i = bumpsX - 1; i >= 0; i--)
        {
            points.Add((r.X + r.Width * i / bumpsX, r.Bottom));
        }

        for (var i = bumpsY - 1; i > 0; i--)
        {
            points.Add((r.X, r.Y + r.Height * i / bumpsY));
        }

        var path = new PathGeometry().MoveTo(points[0].X, points[0].Y);
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            var (dx, dy) = (b.X - a.X, b.Y - a.Y);
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6)
            {
                continue;
            }

            // Bulge outward, perpendicular to the edge (the outline runs clockwise).
            var (nx, ny) = (dy / len, -dx / len);
            var bulge = len * 0.45;
            path.CubicTo(a.X + nx * bulge, a.Y + ny * bulge, b.X + nx * bulge, b.Y + ny * bulge, b.X, b.Y);
        }

        return path.Close();
    }

    /// <summary>Spiky outline, like Mermaid's bang node.</summary>
    private static PathGeometry Bang(Layout.Rect r)
    {
        const int spikes = 14;
        var points = new List<(double X, double Y)>();
        for (var i = 0; i < spikes * 2; i++)
        {
            var angle = Math.PI * i / spikes;
            var scale = i % 2 == 0 ? 1.0 : 0.78;
            points.Add((r.CenterX + r.Width / 2 * scale * Math.Cos(angle), r.CenterY + r.Height / 2 * scale * Math.Sin(angle)));
        }

        return PathGeometry.Polygon(points);
    }
}
