using MkMermaid.Layout;
using MkMermaid.Rendering;
using MkMermaid.Scene;
using MkMermaid.Text;

namespace MkMermaid.Diagrams.Class;

internal static class ClassRenderer
{
    private const double Padding = 10;
    private const double SectionGap = 6;

    private sealed record Box(MeasuredText Header, List<MeasuredText> Attributes, List<MeasuredText> Methods, double Width, double HeaderHeight, double AttributesHeight, double Height);

    public static DiagramScene Render(ClassModel model, RenderContext context, DiagramInfo info)
    {
        var theme = context.Theme;
        var config = context.Config;
        var hideEmpty = config.Flag("class", "hideEmptyMembersBox", false);
        var graph = new LayeredGraph
        {
            Direction = model.Direction,
            NodeSpacing = config.Number("class", "nodeSpacing", 50),
            RankSpacing = config.Number("class", "rankSpacing", 50)
        };

        foreach (var (name, parent) in model.Namespaces)
        {
            var title = context.MeasurePlain(name);
            graph.AddCluster(name, parent, title.Width, title.Height, 10);
        }

        var boxes = new Dictionary<string, Box>(StringComparer.Ordinal);
        foreach (var node in model.Classes.Values.OrderBy(c => c.Order))
        {
            var box = Measure(node, context, hideEmpty);
            boxes[node.Id] = box;
            graph.AddNode(node.Id, box.Width, box.Height, node.Namespace is not null && model.Namespaces.ContainsKey(node.Namespace) ? node.Namespace : null);
        }

        var notes = new List<MeasuredText>();
        for (var i = 0; i < model.Notes.Count; i++)
        {
            var text = context.Measure(model.Notes[i].Text, null, 200, forceWrap: true);
            notes.Add(text);
            var target = model.Notes[i].For;
            graph.AddNode(NoteId(i), text.Width + 2 * Padding, text.Height + 2 * Padding,
                target is not null && model.Classes[target].Namespace is { } ns && model.Namespaces.ContainsKey(ns) ? ns : null);
        }

        var labels = new List<MeasuredText?>();
        foreach (var relation in model.Relations)
        {
            var label = string.IsNullOrEmpty(relation.Label) ? null : context.Measure(relation.Label);
            labels.Add(label);
            graph.AddEdge(relation.From, relation.To, label is null ? 0 : label.Width + 4, label?.Height ?? 0, 1);
        }

        for (var i = 0; i < model.Notes.Count; i++)
        {
            if (model.Notes[i].For is { } target)
            {
                graph.AddEdge(NoteId(i), target, 0, 0, 1);
            }
        }

        var layout = LayeredLayout.Compute(graph);
        var canvas = context.CreateCanvas();
        var background = theme.Hex("background", "#FFFFFF");
        foreach (var cluster in layout.Clusters.Values.OrderBy(c => Depth(model, c.Id)))
        {
            var b = cluster.Bounds;
            canvas.Rect(b.X, b.Y, b.Width, b.Height, theme.Hex("clusterBkg"), theme.Hex("clusterBorder"));
            canvas.Label(context.MeasurePlain(cluster.Id), cluster.LabelBounds.CenterX, cluster.LabelBounds.Y, TextAnchor.Middle, theme.Hex("titleColor"));
        }

        var line = theme.Hex("lineColor");
        var labelColor = theme.Hex("classText", theme.Hex("textColor"));
        for (var i = 0; i < model.Relations.Count; i++)
        {
            var relation = model.Relations[i];
            var routed = layout.Edges[i];
            if (routed.Points.Count < 2)
            {
                continue;
            }

            var points = EdgeDrawer.Clip(routed.Points, Outline(layout, relation.From), Outline(layout, relation.To));
            var visual = new EdgeVisual(line, 1, relation.Dashed ? [3, 3] : null, relation.Start, relation.End, CurveKind.Basis,
                labels[i], labelColor, theme.Hex("edgeLabelBackground"),
                relation.FromCardinality is { Length: > 0 } a ? context.Measure(a, 14) : null,
                relation.ToCardinality is { Length: > 0 } b ? context.Measure(b, 14) : null);
            EdgeDrawer.Draw(canvas, points, visual, routed.LabelCenter, background);
        }

        for (var i = 0; i < model.Notes.Count; i++)
        {
            if (model.Notes[i].For is { } target)
            {
                var routed = layout.Edges[model.Relations.Count + model.Notes.Take(i).Count(n => n.For is not null)];
                var points = EdgeDrawer.Clip(routed.Points, Outline(layout, NoteId(i)), Outline(layout, target));
                EdgeDrawer.Draw(canvas, points, new EdgeVisual(line, 1, [3, 3], Marker.None, Marker.None, CurveKind.Basis), null, background);
            }
        }

        foreach (var node in model.Classes.Values)
        {
            DrawClass(canvas, node, boxes[node.Id], layout.Nodes[node.Id].Bounds, model, context);
        }

        for (var i = 0; i < model.Notes.Count; i++)
        {
            var r = layout.Nodes[NoteId(i)].Bounds;
            canvas.Rect(r.X, r.Y, r.Width, r.Height, theme.Hex("noteBkgColor"), theme.Hex("noteBorderColor"));
            canvas.LabelCentered(notes[i], r.CenterX, r.CenterY, theme.Hex("noteTextColor"));
        }

        canvas.Title(info.Title, theme.Hex("textColor"));
        return canvas.Finish(DiagramKind.Class, info.Title, info.AccDescription, 8, null);
    }

    private static string NoteId(int index) => "\u0001note" + index;

    private static int Depth(ClassModel model, string id)
    {
        var depth = 0;
        for (var p = model.Namespaces.GetValueOrDefault(id); p is not null && depth < 64; p = model.Namespaces.GetValueOrDefault(p))
        {
            depth++;
        }

        return depth;
    }

    private static List<Point>? Outline(LayeredResult layout, string id) =>
        layout.Nodes.TryGetValue(id, out var node) ? Shapes.Outline(NodeShape.Rect, node.Bounds) : null;

    private static Box Measure(ClassNode node, RenderContext context, bool hideEmpty)
    {
        var lines = new List<IReadOnlyList<TextRun>>();
        foreach (var annotation in node.Annotations)
        {
            lines.Add([new TextRun("«" + annotation + "»")]);
        }

        var name = node.Label ?? node.Id;
        lines.Add([new TextRun(node.Generic is { } g ? $"{name}<{g}>" : name, Bold: true)]);
        var header = context.Measure(RichText.FromLines(lines));
        var attributes = node.Attributes.Select(m => Member(m, context)).ToList();
        var methods = node.Methods.Select(m => Member(m, context)).ToList();
        var width = new[] { header.Width }.Concat(attributes.Select(a => a.Width)).Concat(methods.Select(m => m.Width)).Max() + 2 * Padding;
        width = Math.Max(width, 80);
        var headerHeight = header.Height + 2 * Padding;
        var empty = hideEmpty && attributes.Count == 0 && methods.Count == 0;
        var attributesHeight = empty ? 0 : attributes.Sum(a => a.Height) + 2 * SectionGap;
        var methodsHeight = empty ? 0 : methods.Sum(m => m.Height) + 2 * SectionGap;
        return new Box(header, attributes, methods, width, headerHeight, attributesHeight, headerHeight + attributesHeight + methodsHeight);
    }

    private static MeasuredText Member(ClassMember member, RenderContext context) =>
        context.Measure(RichText.FromLines([[new TextRun(member.Text, Italic: member.Abstract, Underline: member.Static)]]));

    private static void DrawClass(Canvas canvas, ClassNode node, Box box, Rect r, ClassModel model, RenderContext context)
    {
        var theme = context.Theme;
        var style = Style.None;
        foreach (var name in node.Classes)
        {
            style = style.Merge(model.ClassDefs.GetValueOrDefault(name));
        }

        if (node.Classes.Count == 0 && model.ClassDefs.TryGetValue("default", out var fallback))
        {
            style = fallback;
        }

        style = style.Merge(node.Style);
        var fill = Style.Resolve(style.Fill, theme.Hex("mainBkg")) ?? "#00000000";
        var stroke = Style.Resolve(style.Stroke, theme.Hex("nodeBorder")) ?? "#00000000";
        var text = style.Color ?? theme.Hex("classText", theme.Hex("textColor"));
        var width = style.StrokeWidth ?? 1;
        canvas.Rect(r.X, r.Y, r.Width, r.Height, fill, stroke, width, dash: style.Dash);
        canvas.Label(box.Header, r.CenterX, r.Y + Padding, TextAnchor.Middle, text);
        if (box.AttributesHeight <= 0)
        {
            return;
        }

        var y = r.Y + box.HeaderHeight;
        canvas.Line(r.X, y, r.Right, y, stroke, width);
        var cursor = y + SectionGap;
        foreach (var attribute in box.Attributes)
        {
            canvas.Label(attribute, r.X + Padding, cursor, TextAnchor.Start, text);
            cursor += attribute.Height;
        }

        y += box.AttributesHeight;
        canvas.Line(r.X, y, r.Right, y, stroke, width);
        cursor = y + SectionGap;
        foreach (var method in box.Methods)
        {
            canvas.Label(method, r.X + Padding, cursor, TextAnchor.Start, text);
            cursor += method.Height;
        }
    }
}
