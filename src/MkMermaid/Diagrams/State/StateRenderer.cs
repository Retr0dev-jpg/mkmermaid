using MkMermaid.Layout;
using MkMermaid.Rendering;
using MkMermaid.Scene;
using MkMermaid.Text;

namespace MkMermaid.Diagrams.State;

internal static class StateRenderer
{
    private const double Padding = 8;
    private const double NoteGap = 20;

    private sealed record Item(StateNode Node, MeasuredText? Label, MeasuredText? Description, MeasuredText? Note, double Width, double Height, double NoteWidth);

    public static DiagramScene Render(StateModel model, RenderContext context, DiagramInfo info)
    {
        var theme = context.Theme;
        var config = context.Config;
        var horizontal = model.Direction is LayoutDirection.LeftToRight or LayoutDirection.RightToLeft;
        var graph = new LayeredGraph
        {
            Direction = model.Direction,
            NodeSpacing = config.Number("state", "nodeSpacing", 50),
            RankSpacing = config.Number("state", "rankSpacing", 50)
        };

        foreach (var state in model.States.Values.Where(s => s.Composite).OrderBy(s => s.Order))
        {
            var title = context.Measure(state.Label ?? state.Id);
            graph.AddCluster(state.Id, ClusterOf(model, state.Scope), title.Width, title.Height, 10);
            foreach (var region in state.Regions)
            {
                graph.AddCluster(region, state.Id, 0, 0, 10);
            }
        }

        var items = new Dictionary<string, Item>(StringComparer.Ordinal);
        foreach (var state in model.States.Values.Where(s => !s.Composite).OrderBy(s => s.Order))
        {
            var item = Measure(state, context, horizontal, StyleOf(model, state).Bold == true);
            items[state.Id] = item;
            graph.AddNode(state.Id, item.Width + item.NoteWidth, item.Height, ClusterOf(model, state.Scope));
        }

        var labels = new List<MeasuredText?>();
        foreach (var t in model.Transitions)
        {
            var label = string.IsNullOrEmpty(t.Label) ? null : context.Measure(t.Label);
            labels.Add(label);
            graph.AddEdge(t.From, t.To, label is null ? 0 : label.Width + 4, label?.Height ?? 0, 1);
        }

        var layout = LayeredLayout.Compute(graph);
        var canvas = context.CreateCanvas();
        var background = theme.Hex("background", "#FFFFFF");

        foreach (var state in model.States.Values.Where(s => s.Composite).OrderBy(s => Depth(model, s)).ThenBy(s => s.Order))
        {
            if (!layout.Clusters.TryGetValue(state.Id, out var cluster))
            {
                continue;
            }

            var b = cluster.Bounds;
            var style = StyleOf(model, state);
            var border = Style.Resolve(style.Stroke, theme.Hex("compositeBorder", theme.Hex("nodeBorder"))) ?? theme.Hex("nodeBorder");
            canvas.Rect(b.X, b.Y, b.Width, b.Height, Style.Resolve(style.Fill, theme.Hex("compositeBackground", theme.Hex("background"))), border, 1, 5);
            var titleHeight = cluster.LabelBounds.Bottom - b.Y + 4;
            canvas.Shape(new PathGeometry().MoveTo(b.X, b.Y + titleHeight).LineTo(b.X + b.Width, b.Y + titleHeight), null, border);
            canvas.Rect(b.X + 0.5, b.Y + 0.5, b.Width - 1, titleHeight - 0.5, theme.Hex("compositeTitleBackground", theme.Hex("mainBkg")), null, 0, 4);
            canvas.Label(context.Measure(state.Label ?? state.Id), cluster.LabelBounds.CenterX, cluster.LabelBounds.Y, TextAnchor.Middle,
                style.Color ?? theme.Hex("stateLabelColor", theme.Hex("textColor")));
            for (var r = 1; r < state.Regions.Count; r++)
            {
                if (layout.Clusters.TryGetValue(state.Regions[r], out var region) && layout.Clusters.TryGetValue(state.Regions[r - 1], out var previous))
                {
                    if (horizontal)
                    {
                        var y = (previous.Bounds.Bottom + region.Bounds.Y) / 2;
                        canvas.Line(b.X, y, b.Right, y, border, 1, [5, 5]);
                    }
                    else
                    {
                        var x = (previous.Bounds.Right + region.Bounds.X) / 2;
                        canvas.Line(x, b.Y + titleHeight, x, b.Bottom, border, 1, [5, 5]);
                    }
                }
            }
        }

        var line = theme.Hex("transitionColor", theme.Hex("lineColor"));
        for (var i = 0; i < model.Transitions.Count; i++)
        {
            var t = model.Transitions[i];
            var routed = layout.Edges[i];
            if (routed.Points.Count < 2)
            {
                continue;
            }

            var points = routed.Points.ToList();
            var fromOutline = Outline(layout, items, t.From, out var fromCenter);
            var toOutline = Outline(layout, items, t.To, out var toCenter);
            if (fromCenter is { } fc)
            {
                points[0] = fc;
            }

            if (toCenter is { } tc)
            {
                points[^1] = tc;
            }

            points = EdgeDrawer.Clip(points, fromOutline, toOutline);
            var visual = new EdgeVisual(line, 1, null, Marker.None, Marker.Arrow, CurveKind.Basis, labels[i],
                theme.Hex("transitionLabelColor", theme.Hex("textColor")), theme.Hex("edgeLabelBackground"));
            EdgeDrawer.Draw(canvas, points, visual, routed.LabelCenter, background);
        }

        foreach (var item in items.Values)
        {
            DrawState(canvas, item, layout.Nodes[item.Node.Id].Bounds, model, context);
        }

        canvas.Title(info.Title, theme.Hex("textColor"));
        return canvas.Finish(DiagramKind.State, info.Title, info.AccDescription, 8, null);
    }

    private static string? ClusterOf(StateModel model, string? scope) =>
        scope is not null && (model.RegionParents.ContainsKey(scope) || model.States.TryGetValue(scope, out var s) && s.Composite) ? scope : null;

    private static int Depth(StateModel model, StateNode state)
    {
        var depth = 0;
        for (var s = state.Scope; s is not null && depth < 64; depth++)
        {
            var composite = model.RegionParents.GetValueOrDefault(s, s);
            s = model.States.TryGetValue(composite, out var parent) ? parent.Scope : null;
        }

        return depth;
    }

    private static Style StyleOf(StateModel model, StateNode state)
    {
        var style = state.Classes.Count == 0 ? model.ClassDefs.GetValueOrDefault("default") ?? Style.None : Style.None;
        foreach (var name in state.Classes)
        {
            style = style.Merge(model.ClassDefs.GetValueOrDefault(name));
        }

        return style.Merge(state.Style);
    }

    private static Item Measure(StateNode state, RenderContext context, bool horizontal, bool bold)
    {
        var note = state.Note is { } n ? context.Measure(n.Text, 14, 200, forceWrap: true) : null;
        var noteWidth = note is null ? 0 : note.Width + 2 * Padding + NoteGap;
        switch (state.Type)
        {
            case StateType.Start or StateType.End:
                return new Item(state, null, null, note, 14, 14, noteWidth);
            case StateType.Fork or StateType.Join:
                return new Item(state, null, null, note, horizontal ? 10 : 70, horizontal ? 70 : 10, noteWidth);
            case StateType.Choice:
                return new Item(state, null, null, note, 28, 28, noteWidth);
        }

        var description = state.Descriptions.Count > 0 ? context.Measure(string.Join("<br>", state.Descriptions)) : null;
        var text = RichText.Parse(state.Label ?? state.Id);
        var label = context.Measure(description is not null || bold ? text.Bold() : text);
        var width = Math.Max(label.Width, description?.Width ?? 0) + 2 * Padding;
        var height = label.Height + 2 * Padding + (description is null ? 0 : description.Height + Padding);
        return new Item(state, label, description, note, Math.Max(width, 40), Math.Max(height, 40), noteWidth);
    }

    /// <summary>Rectangle of the state itself inside its layout box (which also holds the note).</summary>
    private static Rect StateRect(Item item, Rect box) =>
        item.NoteWidth <= 0 ? box
        : new Rect(item.Node.Note!.Left ? box.Right - item.Width : box.X, box.CenterY - item.Height / 2, item.Width, item.Height);

    private static List<Point>? Outline(LayeredResult layout, Dictionary<string, Item> items, string id, out Point? center)
    {
        center = null;
        if (items.TryGetValue(id, out var item) && layout.Nodes.TryGetValue(id, out var node))
        {
            var r = StateRect(item, node.Bounds);
            center = new Point(r.CenterX, r.CenterY);
            var shape = item.Node.Type switch
            {
                StateType.Start or StateType.End => NodeShape.Circle,
                StateType.Choice => NodeShape.Diamond,
                StateType.Fork or StateType.Join => NodeShape.Rect,
                _ => NodeShape.Rounded
            };
            return Shapes.Outline(shape, r);
        }

        return layout.Clusters.TryGetValue(id, out var cluster) ? Shapes.Outline(NodeShape.Rect, cluster.Bounds) : null;
    }

    private static void DrawState(Canvas canvas, Item item, Rect box, StateModel model, RenderContext context)
    {
        var theme = context.Theme;
        var state = item.Node;
        var r = StateRect(item, box);
        var special = theme.Hex("specialStateColor", theme.Hex("lineColor"));
        var style = StyleOf(model, state);
        switch (state.Type)
        {
            case StateType.Start:
                canvas.Circle(r.CenterX, r.CenterY, 7, special, special);
                break;
            case StateType.End:
                canvas.Circle(r.CenterX, r.CenterY, 7, theme.Hex("background", "#FFFFFF"), special, 1.5);
                canvas.Circle(r.CenterX, r.CenterY, 4.5, special, null);
                break;
            case StateType.Fork or StateType.Join:
                canvas.Rect(r.X, r.Y, r.Width, r.Height, special, null, 0, 2);
                break;
            case StateType.Choice:
                Shapes.Draw(canvas, NodeShape.Diamond, r, theme.Hex("mainBkg"), theme.Hex("nodeBorder"), 1);
                break;
            default:
                var fill = Style.Resolve(style.Fill, theme.Hex("stateBkg", theme.Hex("mainBkg"))) ?? "#00000000";
                var stroke = Style.Resolve(style.Stroke, theme.Hex("nodeBorder")) ?? "#00000000";
                var color = style.Color ?? theme.Hex("stateLabelColor", theme.Hex("textColor"));
                canvas.Rect(r.X, r.Y, r.Width, r.Height, fill, stroke, style.StrokeWidth ?? 1, 5, style.Dash);
                if (item.Description is { } description)
                {
                    canvas.Label(item.Label!, r.CenterX, r.Y + Padding, TextAnchor.Middle, color);
                    var y = r.Y + item.Label!.Height + 1.5 * Padding;
                    canvas.Line(r.X, y, r.Right, y, stroke, style.StrokeWidth ?? 1);
                    canvas.Label(description, r.X + Padding, y + Padding / 2, TextAnchor.Start, color);
                }
                else
                {
                    canvas.LabelCentered(item.Label!, r.CenterX, r.CenterY, color);
                }

                break;
        }

        if (item.Note is { } note && state.Note is { } spec)
        {
            var width = note.Width + 2 * Padding;
            var height = note.Height + 2 * Padding;
            var x = spec.Left ? r.X - NoteGap - width : r.Right + NoteGap;
            var y = r.CenterY - height / 2;
            canvas.Rect(x, y, width, height, theme.Hex("noteBkgColor"), theme.Hex("noteBorderColor"));
            canvas.LabelCentered(note, x + width / 2, y + height / 2, theme.Hex("noteTextColor"));
            var from = spec.Left ? x + width : x;
            canvas.Line(from, r.CenterY, spec.Left ? r.X : r.Right, r.CenterY, theme.Hex("noteBorderColor"), 1, [2, 2]);
        }
    }
}
