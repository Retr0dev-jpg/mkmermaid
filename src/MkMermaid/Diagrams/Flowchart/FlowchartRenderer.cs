using MkMermaid.Layout;
using MkMermaid.Rendering;
using MkMermaid.Scene;
using MkMermaid.Text;

namespace MkMermaid.Diagrams.Flowchart;

internal static class FlowchartRenderer
{
    public static DiagramScene Render(FlowchartModel model, RenderContext context, DiagramInfo info)
    {
        var theme = context.Theme;
        var config = context.Config;
        var padding = config.Number("flowchart", "padding", 15);
        var wrapWidth = config.Number("flowchart", "wrappingWidth", 200);
        var graph = new LayeredGraph
        {
            Direction = model.Direction,
            NodeSpacing = config.Number("flowchart", "nodeSpacing", 50),
            RankSpacing = config.Number("flowchart", "rankSpacing", 50),
            EdgeSpacing = 10
        };

        var defaultClass = model.ClassDefs.GetValueOrDefault("default") ?? Style.None;
        var nodeStyles = new Dictionary<string, Style>(StringComparer.Ordinal);
        var nodeLabels = new Dictionary<string, MeasuredText>(StringComparer.Ordinal);
        var subgraphStyles = new Dictionary<string, Style>(StringComparer.Ordinal);
        var subgraphTitles = new Dictionary<string, MeasuredText>(StringComparer.Ordinal);
        foreach (var subgraph in model.Subgraphs.Values.OrderBy(s => s.Order))
        {
            var style = Style.None;
            foreach (var name in subgraph.Classes)
            {
                style = style.Merge(model.ClassDefs.GetValueOrDefault(name));
            }

            style = style.Merge(subgraph.Style);
            var text = RichText.Parse(subgraph.Title);
            var title = context.Measure(style.Bold == true ? text.Bold() : text, style.FontSize, wrapWidth * 1.5, forceWrap: true);
            subgraphStyles[subgraph.Id] = style;
            subgraphTitles[subgraph.Id] = title;
            graph.AddCluster(subgraph.Id, subgraph.Parent, title.Width, title.Height, 8, subgraph.Direction);
        }

        foreach (var node in model.Nodes.Values.OrderBy(n => n.Order))
        {
            var style = node.Classes.Count == 0 ? defaultClass : Style.None;
            foreach (var name in node.Classes)
            {
                style = style.Merge(model.ClassDefs.GetValueOrDefault(name));
            }

            style = style.Merge(node.Style);
            nodeStyles[node.Id] = style;
            var text = RichText.Parse(node.Label ?? node.Id);
            var label = context.Measure(style.Bold == true ? text.Bold() : text, style.FontSize, wrapWidth, forceWrap: true);
            nodeLabels[node.Id] = label;
            var (w, h) = node.Shape is NodeShape.SmallCircle or NodeShape.FilledCircle or NodeShape.FramedCircle or NodeShape.ForkBar
                ? Shapes.Size(node.Shape, 0, 0, 0)
                : Shapes.Size(node.Shape, label.Width, label.Height, padding);
            graph.AddNode(node.Id, w, h, node.Subgraph is not null && model.Subgraphs.ContainsKey(node.Subgraph) ? node.Subgraph : null);
        }

        var edgeLabels = new List<MeasuredText?>();
        foreach (var edge in model.Edges)
        {
            var label = string.IsNullOrEmpty(edge.Label) ? null : context.Measure(RichText.Parse(edge.Label), null, wrapWidth, forceWrap: true);
            edgeLabels.Add(label);
            graph.AddEdge(edge.From, edge.To, label is null ? 0 : label.Width + 4, label?.Height ?? 0, edge.Length);
        }

        var layout = LayeredLayout.Compute(graph);
        var canvas = context.CreateCanvas();
        var background = theme.Hex("background", "#FFFFFF");

        foreach (var subgraph in model.Subgraphs.Values.OrderBy(s => Depth(model, s)).ThenBy(s => s.Order))
        {
            if (!layout.Clusters.TryGetValue(subgraph.Id, out var cluster))
            {
                continue;
            }

            var style = subgraphStyles[subgraph.Id];
            var b = cluster.Bounds;
            canvas.Rect(b.X, b.Y, b.Width, b.Height,
                Style.Resolve(style.Fill, theme.Hex("clusterBkg")), Style.Resolve(style.Stroke, theme.Hex("clusterBorder")),
                style.StrokeWidth ?? 1, style.Radius ?? 0, style.Dash, style.Opacity ?? 1);
            canvas.Label(subgraphTitles[subgraph.Id], cluster.LabelBounds.CenterX, cluster.LabelBounds.Y, TextAnchor.Middle, style.Color ?? theme.Hex("titleColor"));
        }

        var curve = Curves.Parse(model.DefaultCurve ?? config.Text("flowchart", "curve"));
        for (var i = 0; i < model.Edges.Count; i++)
        {
            var edge = model.Edges[i];
            var routed = layout.Edges[i];
            if (routed.Points.Count < 2)
            {
                continue;
            }

            var style = model.DefaultLinkStyle.Merge(edge.Style);
            var stroke = Style.Resolve(style.Stroke, theme.Hex("lineColor")) ?? theme.Hex("lineColor");
            var width = style.StrokeWidth ?? edge.Line switch { LineKind.Thick => 3.5, _ => 2 };
            var dash = style.Dash ?? (edge.Line == LineKind.Dotted ? [3, 3] : null);
            var points = EdgeDrawer.Clip(routed.Points, Outline(layout, model, edge.From), Outline(layout, model, edge.To));
            var visual = new EdgeVisual(
                stroke, width, dash, edge.Start, edge.End,
                edge.Curve is not null ? Curves.Parse(edge.Curve) : curve,
                edgeLabels[i], style.Color ?? theme.Hex("textColor"), theme.Hex("edgeLabelBackground"),
                Visible: edge.Line != LineKind.Invisible);
            EdgeDrawer.Draw(canvas, points, visual, routed.LabelCenter, background);
        }

        foreach (var node in model.Nodes.Values.OrderBy(n => n.Order))
        {
            var rect = layout.Nodes[node.Id].Bounds;
            var style = nodeStyles[node.Id];
            var fill = Style.Resolve(style.Fill, theme.Hex("mainBkg")) ?? "#00000000";
            var stroke = Style.Resolve(style.Stroke, theme.Hex("nodeBorder")) ?? "#00000000";
            Shapes.Draw(canvas, node.Shape, rect, fill, stroke, style.StrokeWidth ?? 1, style.Dash);
            if (node.Shape is NodeShape.SmallCircle or NodeShape.FilledCircle or NodeShape.FramedCircle or NodeShape.ForkBar)
            {
                continue;
            }

            var label = nodeLabels[node.Id];
            var color = style.Color ?? theme.Hex("nodeTextColor");
            var cy = node.Shape is NodeShape.Cylinder or NodeShape.LinedCylinder ? rect.CenterY + rect.Height * 0.06 : rect.CenterY;
            canvas.Label(label, rect.CenterX, cy - label.Height / 2, TextAnchor.Middle, color);
        }

        canvas.Title(info.Title, theme.Hex("textColor"));
        return canvas.Finish(DiagramKind.Flowchart, info.Title, info.AccDescription, config.Number("flowchart", "diagramPadding", 8), null);
    }

    private static int Depth(FlowchartModel model, FlowSubgraph subgraph)
    {
        var depth = 0;
        for (var p = subgraph.Parent; p is not null && depth < 64; p = model.Subgraphs.GetValueOrDefault(p)?.Parent)
        {
            depth++;
        }

        return depth;
    }

    private static List<Point>? Outline(LayeredResult layout, FlowchartModel model, string id)
    {
        if (layout.Nodes.TryGetValue(id, out var node))
        {
            return Shapes.Outline(model.Nodes[id].Shape, node.Bounds);
        }

        return layout.Clusters.TryGetValue(id, out var cluster) ? Shapes.Outline(NodeShape.Rect, cluster.Bounds) : null;
    }
}
