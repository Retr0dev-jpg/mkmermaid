using System.Text.RegularExpressions;
using MkMermaid.Diagrams.Flowchart;
using MkMermaid.Layout;
using MkMermaid.Parsing;
using MkMermaid.Rendering;
using MkMermaid.Scene;
using MkMermaid.Text;
using static MkMermaid.Parsing.ParseHelpers;

namespace MkMermaid.Diagrams.Requirement;

internal sealed class RequirementBox(string name, string kind, int order)
{
    public string Name { get; } = name;

    /// <summary>The keyword as written (requirement, functionalRequirement, element, ...).</summary>
    public string Kind { get; } = kind;

    public int Order { get; } = order;

    public bool IsElement => Kind == "element";

    public Dictionary<string, string> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Classes { get; } = [];

    public Style Style { get; set; } = Style.None;
}

internal sealed record RequirementRelation(string From, string To, string Kind);

internal sealed class RequirementModel : IDiagramModel
{
    public string? Title => null;

    public LayoutDirection Direction { get; set; } = LayoutDirection.TopToBottom;

    public Dictionary<string, RequirementBox> Boxes { get; } = new(StringComparer.Ordinal);

    public List<RequirementRelation> Relations { get; } = [];

    public Dictionary<string, Style> ClassDefs { get; } = new(StringComparer.Ordinal);

    public DiagramScene Render(RenderContext context, DiagramInfo info) => RequirementRenderer.Render(this, context, info);
}

internal static partial class RequirementParser
{
    internal static readonly string[] RequirementKinds =
        ["requirement", "functionalRequirement", "interfaceRequirement", "performanceRequirement", "physicalRequirement", "designConstraint"];

    private static readonly string[] RelationKinds = ["contains", "copies", "derives", "satisfies", "verifies", "refines", "traces"];

    [GeneratedRegex(@"^(?<kind>\w+)\s+(?<name>""[^""]+""|[^\s{:]+)\s*(?::::(?<classes>[\w,-]+))?\s*\{\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex BoxStart();

    [GeneratedRegex(@"^(?<a>""[^""]+""|[^\s<-]+)\s*(?:-\s*(?<kind>\w+)\s*->|<-\s*(?<rkind>\w+)\s*-)\s*(?<b>""[^""]+""|\S+)$", RegexOptions.CultureInvariant)]
    private static partial Regex Relation();

    public static RequirementModel Parse(Source source)
    {
        var model = new RequirementModel();
        RequirementBox? open = null;
        foreach (var line in source.Lines)
        {
            var text = line.Trimmed;
            if (open is not null)
            {
                if (text == "}")
                {
                    open = null;
                    continue;
                }

                var colon = text.IndexOf(':');
                if (colon <= 0)
                {
                    throw Error(line, $"Expected 'field: value' inside {open.Name}.");
                }

                open.Fields[text[..colon].Trim()] = Unquote(text[(colon + 1)..].Trim());
                continue;
            }

            var box = BoxStart().Match(text);
            if (box.Success)
            {
                var kind = box.Groups["kind"].Value;
                if (kind != "element" && !RequirementKinds.Contains(kind, StringComparer.Ordinal))
                {
                    throw Error(line, $"Unknown block type '{Source.Truncate(kind)}'.");
                }

                CheckCount(model.Boxes.Count + 1, MermaidEngine.MaxElements, line.Number, "requirements");
                var name = Unquote(box.Groups["name"].Value);
                open = new RequirementBox(name, kind, model.Boxes.Count);
                if (model.Boxes.TryGetValue(name, out var previous))
                {
                    open.Classes.AddRange(previous.Classes);
                    open.Style = previous.Style;
                }

                model.Boxes[name] = open;
                if (box.Groups["classes"].Success)
                {
                    open.Classes.AddRange(box.Groups["classes"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries));
                }

                continue;
            }

            if (StartsWithWord(text, "direction"))
            {
                model.Direction = FlowchartModel.ParseDirection(text[9..]) ?? throw Error(line, "direction must be TB, BT, LR or RL.");
                continue;
            }

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
                        Ensure(model, id.Trim()).Classes.AddRange(parts[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
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
                        var target = Ensure(model, id.Trim());
                        target.Style = target.Style.Merge(Style.Parse(parts[1]));
                    }
                }

                continue;
            }

            var relation = Relation().Match(text);
            if (relation.Success)
            {
                var forward = relation.Groups["kind"].Success;
                var kind = (forward ? relation.Groups["kind"].Value : relation.Groups["rkind"].Value).ToLowerInvariant();
                if (!RelationKinds.Contains(kind, StringComparer.Ordinal))
                {
                    throw Error(line, $"Unknown relationship '{Source.Truncate(kind)}'; use contains, copies, derives, satisfies, verifies, refines or traces.");
                }

                var a = Unquote(relation.Groups["a"].Value);
                var b = Unquote(relation.Groups["b"].Value);
                CheckCount(model.Relations.Count + 1, MermaidEngine.MaxElements, line.Number, "relationships");
                model.Relations.Add(forward ? new RequirementRelation(a, b, kind) : new RequirementRelation(b, a, kind));
                continue;
            }

            throw Error(line, $"Unrecognized statement '{Source.Truncate(text)}'.");
        }

        if (open is not null)
        {
            throw Error(source.Lines.Count == 0 ? source.HeaderLine : source.Lines[^1].Number, $"'{Source.Truncate(open.Name)}' is missing its closing brace.");
        }

        foreach (var relation in model.Relations)
        {
            foreach (var end in new[] { relation.From, relation.To })
            {
                if (!model.Boxes.ContainsKey(end))
                {
                    throw new MermaidParseException(MermaidErrorCode.UnknownReference, $"Relationship refers to '{Source.Truncate(end)}', which is not declared.", source.HeaderLine);
                }
            }
        }

        return model;
    }

    private static RequirementBox Ensure(RequirementModel model, string name)
    {
        if (!model.Boxes.TryGetValue(name, out var box))
        {
            // Styles may come before the declaration; the declaration keeps them.
            box = new RequirementBox(name, "element", model.Boxes.Count);
            model.Boxes[name] = box;
        }

        return box;
    }
}

internal static class RequirementRenderer
{
    private const double Padding = 10;

    private sealed record Card(MeasuredText Kind, MeasuredText Name, List<MeasuredText> Rows, double Width, double Height);

    public static DiagramScene Render(RequirementModel model, RenderContext context, DiagramInfo info)
    {
        var theme = context.Theme;
        var config = context.Config;
        var graph = new LayeredGraph
        {
            Direction = model.Direction,
            NodeSpacing = config.Number("requirement", "nodeSpacing", 50),
            RankSpacing = config.Number("requirement", "rankSpacing", 50)
        };

        var cards = new Dictionary<string, Card>(StringComparer.Ordinal);
        foreach (var box in model.Boxes.Values.OrderBy(b => b.Order))
        {
            var card = Measure(box, context);
            cards[box.Name] = card;
            graph.AddNode(box.Name, card.Width, card.Height);
        }

        var labels = model.Relations.Select(r => context.MeasurePlain($"<<{r.Kind}>>", context.FontSize * 0.85)).ToList();
        for (var i = 0; i < model.Relations.Count; i++)
        {
            graph.AddEdge(model.Relations[i].From, model.Relations[i].To, labels[i].Width + 8, labels[i].Height);
        }

        var layout = LayeredLayout.Compute(graph);
        var canvas = context.CreateCanvas();
        var background = theme.Hex("background", "#FFFFFF");
        var line = theme.Hex("relationColor", theme.Hex("lineColor"));
        for (var i = 0; i < model.Relations.Count; i++)
        {
            var relation = model.Relations[i];
            var routed = layout.Edges[i];
            if (routed.Points.Count < 2)
            {
                continue;
            }

            var points = EdgeDrawer.Clip(routed.Points,
                Shapes.Outline(NodeShape.Rect, layout.Nodes[relation.From].Bounds), Shapes.Outline(NodeShape.Rect, layout.Nodes[relation.To].Bounds));
            var contains = relation.Kind == "contains";
            var visual = new EdgeVisual(line, 1, contains ? null : [10, 7], contains ? Marker.Contains : Marker.None, contains ? Marker.None : Marker.OpenArrow,
                CurveKind.Basis, labels[i], theme.Hex("relationLabelColor", theme.Hex("textColor")), theme.Hex("relationLabelBackground", theme.Hex("edgeLabelBackground")));
            EdgeDrawer.Draw(canvas, points, visual, routed.LabelCenter, background);
        }

        foreach (var box in model.Boxes.Values)
        {
            Draw(canvas, box, cards[box.Name], layout.Nodes[box.Name].Bounds, model, context);
        }

        canvas.Title(info.Title, theme.Hex("textColor"));
        return canvas.Finish(DiagramKind.Requirement, info.Title, info.AccDescription, 8, null);
    }

    private static Card Measure(RequirementBox box, RenderContext context)
    {
        var kind = context.MeasurePlain($"<<{KindLabel(box.Kind)}>>");
        var name = context.Measure(RichText.Parse(box.Name).Bold(), null, 300, forceWrap: true);
        var rows = new List<MeasuredText>();
        foreach (var (label, value) in Rows(box))
        {
            rows.Add(context.Measure($"{label}: {value}", null, 300, forceWrap: true));
        }

        var width = Math.Max(kind.Width, Math.Max(name.Width, rows.Count == 0 ? 0 : rows.Max(r => r.Width))) + 2 * Padding;
        var height = kind.Height + name.Height + 2 * Padding + (rows.Count == 0 ? 0 : rows.Sum(r => r.Height) + Padding * 1.5);
        return new Card(kind, name, rows, Math.Max(width, 150), height);
    }

    private static IEnumerable<(string Label, string Value)> Rows(RequirementBox box)
    {
        if (box.IsElement)
        {
            if (box.Fields.TryGetValue("type", out var type))
            {
                yield return ("Type", type);
            }

            if (box.Fields.TryGetValue("docref", out var docref))
            {
                yield return ("Doc Ref", docref);
            }

            yield break;
        }

        if (box.Fields.TryGetValue("id", out var id))
        {
            yield return ("ID", id);
        }

        if (box.Fields.TryGetValue("text", out var text))
        {
            yield return ("Text", text);
        }

        if (box.Fields.TryGetValue("risk", out var risk))
        {
            yield return ("Risk", Capitalize(risk));
        }

        if (box.Fields.TryGetValue("verifymethod", out var method))
        {
            yield return ("Verification", Capitalize(method));
        }
    }

    private static string KindLabel(string kind) => kind switch
    {
        "functionalRequirement" => "Functional Requirement",
        "interfaceRequirement" => "Interface Requirement",
        "performanceRequirement" => "Performance Requirement",
        "physicalRequirement" => "Physical Requirement",
        "designConstraint" => "Design Constraint",
        "element" => "Element",
        _ => "Requirement"
    };

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..].ToLowerInvariant();

    private static void Draw(Canvas canvas, RequirementBox box, Card card, Rect r, RequirementModel model, RenderContext context)
    {
        var theme = context.Theme;
        var style = box.Classes.Count == 0 ? model.ClassDefs.GetValueOrDefault("default") ?? Style.None : Style.None;
        foreach (var name in box.Classes)
        {
            style = style.Merge(model.ClassDefs.GetValueOrDefault(name));
        }

        style = style.Merge(box.Style);
        var fill = Style.Resolve(style.Fill, theme.Hex("requirementBackground")) ?? "#00000000";
        var stroke = Style.Resolve(style.Stroke, theme.Hex("requirementBorderColor")) ?? "#00000000";
        var color = style.Color ?? theme.Hex("requirementTextColor", theme.Hex("textColor"));
        canvas.Rect(r.X, r.Y, r.Width, r.Height, fill, stroke, style.StrokeWidth ?? theme.Number("requirementBorderSize", 1), dash: style.Dash);
        var y = r.Y + Padding;
        canvas.Label(card.Kind, r.CenterX, y, TextAnchor.Middle, color);
        y += card.Kind.Height;
        canvas.Label(card.Name, r.CenterX, y, TextAnchor.Middle, color);
        y += card.Name.Height + Padding / 2;
        if (card.Rows.Count == 0)
        {
            return;
        }

        canvas.Line(r.X, y, r.Right, y, stroke, style.StrokeWidth ?? theme.Number("requirementBorderSize", 1));
        y += Padding;
        foreach (var row in card.Rows)
        {
            canvas.Label(row, r.X + Padding, y, TextAnchor.Start, color);
            y += row.Height;
        }
    }
}
