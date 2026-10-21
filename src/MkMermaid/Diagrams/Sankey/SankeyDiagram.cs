using System.Globalization;
using System.Text;
using MkMermaid.Parsing;
using MkMermaid.Rendering;
using MkMermaid.Scene;
using static MkMermaid.Parsing.ParseHelpers;

namespace MkMermaid.Diagrams.Sankey;

internal sealed record SankeyFlow(string Source, string Target, double Value);

internal sealed class SankeyModel : IDiagramModel
{
    public string? Title => null;

    /// <summary>Node names in first-seen order.</summary>
    public List<string> Nodes { get; } = [];

    public List<SankeyFlow> Flows { get; } = [];

    public DiagramScene Render(RenderContext context, DiagramInfo info) => SankeyRenderer.Render(this, context, info);
}

internal static class SankeyParser
{
    public static SankeyModel Parse(Source source)
    {
        var model = new SankeyModel();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var targets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var line in source.Lines)
        {
            var fields = Csv(line);
            if (fields.Count != 3)
            {
                throw Error(line, "Each line is source,target,value.");
            }

            if (!TryNumber(fields[2], out var value) || value < 0)
            {
                throw new MermaidParseException(MermaidErrorCode.InvalidValue, $"'{Source.Truncate(fields[2])}' is not a positive number.", line.Number);
            }

            CheckCount(model.Flows.Count + 1, MermaidEngine.MaxElements, line.Number, "flows");
            foreach (var name in fields.Take(2))
            {
                if (seen.Add(name))
                {
                    model.Nodes.Add(name);
                }
            }

            if (Reaches(targets, fields[1], fields[0]))
            {
                throw new MermaidParseException(MermaidErrorCode.InvalidValue, "This flow closes a cycle; a sankey diagram needs flows that only go forward.", line.Number);
            }

            if (!targets.TryGetValue(fields[0], out var next))
            {
                targets[fields[0]] = next = new HashSet<string>(StringComparer.Ordinal);
            }

            next.Add(fields[1]);
            model.Flows.Add(new SankeyFlow(fields[0], fields[1], value));
        }

        return model;
    }

    private static bool Reaches(Dictionary<string, HashSet<string>> targets, string from, string to)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>([from]);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node == to)
            {
                return true;
            }

            if (visited.Add(node) && targets.TryGetValue(node, out var next))
            {
                foreach (var target in next)
                {
                    stack.Push(target);
                }
            }
        }

        return false;
    }

    /// <summary>RFC 4180 fields: quotes allow commas, and a doubled quote is a literal quote.</summary>
    private static List<string> Csv(SourceLine line)
    {
        var fields = new List<string>();
        var text = line.Trimmed;
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        if (quoted)
        {
            throw Error(line, "Unterminated quote.");
        }

        fields.Add(current.ToString().Trim());
        return fields;
    }
}

/// <summary>Port of the d3-sankey layout (justify alignment, six relaxation passes).</summary>
internal static class SankeyRenderer
{
    private static readonly string[] Tableau10 = ["#4E79A7", "#F28E2C", "#E15759", "#76B7B2", "#59A14F", "#EDC949", "#AF7AA1", "#FF9DA7", "#9C755F", "#BAB0AB"];

    private sealed class Node(string id, int index)
    {
        public string Id { get; } = id;

        public int Index { get; } = index;

        public List<Link> Sources { get; } = [];

        public List<Link> Targets { get; } = [];

        public double Value { get; set; }

        public int Depth { get; set; }

        public int Height { get; set; }

        public int Layer { get; set; }

        public double X0 { get; set; }

        public double X1 { get; set; }

        public double Y0 { get; set; }

        public double Y1 { get; set; }
    }

    private sealed class Link(Node source, Node target, double value)
    {
        public Node Source { get; } = source;

        public Node Target { get; } = target;

        public double Value { get; } = value;

        public double Width { get; set; }

        public double Y0 { get; set; }

        public double Y1 { get; set; }
    }

    public static DiagramScene Render(SankeyModel model, RenderContext context, DiagramInfo info)
    {
        var theme = context.Theme;
        var config = context.Config;
        var width = config.Number("sankey", "width", 600);
        var height = config.Number("sankey", "height", 400);
        var showValues = config.Flag("sankey", "showValues", true);
        var prefix = config.Text("sankey", "prefix") ?? "";
        var suffix = config.Text("sankey", "suffix") ?? "";
        var linkColor = config.Text("sankey", "linkColor") ?? "gradient";
        var alignment = config.Text("sankey", "nodeAlignment") ?? "justify";
        const double nodeWidth = 10;
        var padding = 10 + (showValues ? 15 : 0);

        var nodes = model.Nodes.Select((n, i) => new Node(n, i)).ToList();
        var byId = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var links = new List<Link>();
        foreach (var flow in model.Flows)
        {
            var link = new Link(byId[flow.Source], byId[flow.Target], flow.Value);
            link.Source.Sources.Add(link);
            link.Target.Targets.Add(link);
            links.Add(link);
        }

        foreach (var node in nodes)
        {
            node.Value = Math.Max(node.Sources.Sum(l => l.Value), node.Targets.Sum(l => l.Value));
        }

        Depths(nodes, n => n.Sources.Select(l => l.Target), (n, d) => n.Depth = d);
        Depths(nodes, n => n.Targets.Select(l => l.Source), (n, d) => n.Height = d);
        var columnsCount = nodes.Count == 0 ? 0 : nodes.Max(n => n.Depth) + 1;
        var kx = columnsCount <= 1 ? 0 : (width - nodeWidth) / (columnsCount - 1);
        foreach (var node in nodes)
        {
            var layer = alignment switch
            {
                "left" => node.Depth,
                "right" => columnsCount - 1 - node.Height,
                "center" => node.Targets.Count > 0 ? node.Depth : node.Sources.Count > 0 ? node.Sources.Min(l => l.Target.Depth) - 1 : 0,
                _ => node.Sources.Count > 0 ? node.Depth : columnsCount - 1
            };
            node.Layer = Math.Clamp(layer, 0, Math.Max(0, columnsCount - 1));
            node.X0 = node.Layer * kx;
            node.X1 = node.X0 + nodeWidth;
        }

        var columns = nodes.GroupBy(n => n.Layer).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();
        InitializeBreadths(columns, links, height, padding);
        for (var i = 0; i < 6; i++)
        {
            var alpha = Math.Pow(0.99, i);
            var beta = Math.Max(1 - alpha, (i + 1) / 6.0);
            RelaxRightToLeft(columns, alpha, beta, padding, height);
            RelaxLeftToRight(columns, alpha, beta, padding, height);
        }

        foreach (var node in nodes)
        {
            double y0 = node.Y0, y1 = node.Y0;
            foreach (var link in node.Sources)
            {
                link.Y0 = y0 + link.Width / 2;
                y0 += link.Width;
            }

            foreach (var link in node.Targets)
            {
                link.Y1 = y1 + link.Width / 2;
                y1 += link.Width;
            }
        }

        var canvas = context.CreateCanvas();
        string Color(Node n) => Tableau10[n.Index % Tableau10.Length];
        foreach (var link in links)
        {
            var (x0, x1) = (link.Source.X1, link.Target.X0);
            var mid = (x0 + x1) / 2;
            var path = new PathGeometry().MoveTo(x0, link.Y0).CubicTo(mid, link.Y0, mid, link.Y1, x1, link.Y1);
            var width1 = Math.Max(1, link.Width);
            switch (linkColor)
            {
                case "source":
                    canvas.Shape(path, null, Color(link.Source), width1, opacity: 0.5);
                    break;
                case "target":
                    canvas.Shape(path, null, Color(link.Target), width1, opacity: 0.5);
                    break;
                case "gradient":
                    canvas.GradientStroke(path, new LinearGradient(x0, 0, x1, 0, Color(link.Source), Color(link.Target)), width1, 0.5);
                    break;
                default:
                    canvas.Shape(path, null, Rendering.Style.Resolve(linkColor, "#A0A0A0") ?? "#A0A0A0", width1, opacity: 0.5);
                    break;
            }
        }

        var textColor = theme.Hex("textColor");
        foreach (var node in nodes)
        {
            canvas.Rect(node.X0, node.Y0, node.X1 - node.X0, Math.Max(0.5, node.Y1 - node.Y0), Color(node), null, 0);
            var label = showValues
                ? $"{node.Id} {prefix}{(Math.Round(node.Value * 100) / 100).ToString(CultureInfo.InvariantCulture)}{suffix}"
                : node.Id;
            var left = node.X0 < width / 2;
            canvas.Text(label, left ? node.X1 + 6 : node.X0 - 6, (node.Y0 + node.Y1) / 2 + 14 * 0.35, 14, left ? TextAnchor.Start : TextAnchor.End, textColor);
        }

        canvas.Title(info.Title, textColor);
        return canvas.Finish(DiagramKind.Sankey, info.Title, info.AccDescription, 5, null);
    }

    /// <summary>Longest-path depth along <paramref name="next"/>; a cycle is reported as an error.</summary>
    private static void Depths(List<Node> nodes, Func<Node, IEnumerable<Node>> next, Action<Node, int> set)
    {
        var current = new HashSet<Node>(nodes);
        var depth = 0;
        while (current.Count > 0)
        {
            if (depth > nodes.Count)
            {
                throw new MermaidParseException(MermaidErrorCode.InvalidValue, "The flows contain a cycle; a sankey diagram needs flows that only go forward.", 0);
            }

            var following = new HashSet<Node>();
            foreach (var node in current)
            {
                set(node, depth);
                foreach (var n in next(node))
                {
                    following.Add(n);
                }
            }

            current = following;
            depth++;
        }
    }

    private static void InitializeBreadths(List<List<Node>> columns, List<Link> links, double height, double padding)
    {
        var ky = columns.Count == 0
            ? 1
            : columns.Min(c => c.Sum(n => n.Value) is var total and > 0 ? (height - (c.Count - 1) * padding) / total : double.PositiveInfinity);
        if (!double.IsFinite(ky) || ky < 0)
        {
            ky = 0;
        }

        foreach (var column in columns)
        {
            var y = 0.0;
            foreach (var node in column)
            {
                node.Y0 = y;
                node.Y1 = y + node.Value * ky;
                y = node.Y1 + padding;
            }

            var spare = (height - y + padding) / (column.Count + 1);
            for (var i = 0; i < column.Count; i++)
            {
                column[i].Y0 += spare * (i + 1);
                column[i].Y1 += spare * (i + 1);
            }

            ReorderLinks(column);
        }

        foreach (var link in links)
        {
            link.Width = link.Value * ky;
        }
    }

    private static void RelaxLeftToRight(List<List<Node>> columns, double alpha, double beta, double padding, double height)
    {
        for (var i = 1; i < columns.Count; i++)
        {
            foreach (var target in columns[i])
            {
                double y = 0, w = 0;
                foreach (var link in target.Targets)
                {
                    var v = link.Value * (target.Layer - link.Source.Layer);
                    y += TargetTop(link.Source, target, padding) * v;
                    w += v;
                }

                if (w > 0)
                {
                    var dy = (y / w - target.Y0) * alpha;
                    target.Y0 += dy;
                    target.Y1 += dy;
                    ReorderNodeLinks(target);
                }
            }

            columns[i].Sort(AscendingBreadth);
            ResolveCollisions(columns[i], beta, padding, height);
        }
    }

    private static void RelaxRightToLeft(List<List<Node>> columns, double alpha, double beta, double padding, double height)
    {
        for (var i = columns.Count - 2; i >= 0; i--)
        {
            foreach (var source in columns[i])
            {
                double y = 0, w = 0;
                foreach (var link in source.Sources)
                {
                    var v = link.Value * (link.Target.Layer - source.Layer);
                    y += SourceTop(source, link.Target, padding) * v;
                    w += v;
                }

                if (w > 0)
                {
                    var dy = (y / w - source.Y0) * alpha;
                    source.Y0 += dy;
                    source.Y1 += dy;
                    ReorderNodeLinks(source);
                }
            }

            columns[i].Sort(AscendingBreadth);
            ResolveCollisions(columns[i], beta, padding, height);
        }
    }

    private static void ResolveCollisions(List<Node> nodes, double alpha, double padding, double height)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        var i = nodes.Count >> 1;
        var subject = nodes[i];
        BottomToTop(nodes, subject.Y0 - padding, i - 1, alpha, padding);
        TopToBottom(nodes, subject.Y1 + padding, i + 1, alpha, padding);
        BottomToTop(nodes, height, nodes.Count - 1, alpha, padding);
        TopToBottom(nodes, 0, 0, alpha, padding);
    }

    private static void TopToBottom(List<Node> nodes, double y, int i, double alpha, double padding)
    {
        for (; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var dy = (y - node.Y0) * alpha;
            if (dy > 1e-6)
            {
                node.Y0 += dy;
                node.Y1 += dy;
            }

            y = node.Y1 + padding;
        }
    }

    private static void BottomToTop(List<Node> nodes, double y, int i, double alpha, double padding)
    {
        for (; i >= 0; i--)
        {
            var node = nodes[i];
            var dy = (node.Y1 - y) * alpha;
            if (dy > 1e-6)
            {
                node.Y0 -= dy;
                node.Y1 -= dy;
            }

            y = node.Y0 - padding;
        }
    }

    private static double TargetTop(Node source, Node target, double padding)
    {
        var y = source.Y0 - (source.Sources.Count - 1) * padding / 2;
        foreach (var link in source.Sources)
        {
            if (link.Target == target)
            {
                break;
            }

            y += link.Width + padding;
        }

        foreach (var link in target.Targets)
        {
            if (link.Source == source)
            {
                break;
            }

            y -= link.Width;
        }

        return y;
    }

    private static double SourceTop(Node source, Node target, double padding)
    {
        var y = target.Y0 - (target.Targets.Count - 1) * padding / 2;
        foreach (var link in target.Targets)
        {
            if (link.Source == source)
            {
                break;
            }

            y += link.Width + padding;
        }

        foreach (var link in source.Sources)
        {
            if (link.Target == target)
            {
                break;
            }

            y -= link.Width;
        }

        return y;
    }

    private static void ReorderNodeLinks(Node node)
    {
        foreach (var link in node.Targets)
        {
            link.Source.Sources.Sort(AscendingTargetBreadth);
        }

        foreach (var link in node.Sources)
        {
            link.Target.Targets.Sort(AscendingSourceBreadth);
        }
    }

    private static void ReorderLinks(List<Node> nodes)
    {
        foreach (var node in nodes)
        {
            node.Sources.Sort(AscendingTargetBreadth);
            node.Targets.Sort(AscendingSourceBreadth);
        }
    }

    private static int AscendingBreadth(Node a, Node b) => a.Y0.CompareTo(b.Y0) is var c and not 0 ? c : a.Index.CompareTo(b.Index);

    private static int AscendingSourceBreadth(Link a, Link b) => AscendingBreadth(a.Source, b.Source);

    private static int AscendingTargetBreadth(Link a, Link b) => AscendingBreadth(a.Target, b.Target);
}
