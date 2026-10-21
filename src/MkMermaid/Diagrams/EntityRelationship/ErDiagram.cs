using System.Text.RegularExpressions;
using MkMermaid.Diagrams.Flowchart;
using MkMermaid.Layout;
using MkMermaid.Parsing;
using MkMermaid.Rendering;
using MkMermaid.Scene;
using MkMermaid.Text;
using static MkMermaid.Parsing.ParseHelpers;

namespace MkMermaid.Diagrams.EntityRelationship;

internal sealed record ErAttribute(string Type, string Name, string Keys, string Comment);

internal sealed class ErEntity(string id, int order)
{
    public string Id { get; } = id;

    public int Order { get; } = order;

    public string? Alias { get; set; }

    public List<ErAttribute> Attributes { get; } = [];

    public List<string> Classes { get; } = [];

    public Style? Style { get; set; }
}

internal sealed record ErRelation(string From, string To, Marker FromMarker, Marker ToMarker, bool Identifying, string Label);

internal sealed class ErModel : IDiagramModel
{
    public string? Title => null;

    public LayoutDirection Direction { get; set; } = LayoutDirection.TopToBottom;

    public Dictionary<string, ErEntity> Entities { get; } = new(StringComparer.Ordinal);

    public List<ErRelation> Relations { get; } = [];

    public Dictionary<string, Style> ClassDefs { get; } = new(StringComparer.Ordinal);

    public ErEntity Ensure(string id)
    {
        if (!Entities.TryGetValue(id, out var entity))
        {
            entity = new ErEntity(id, Entities.Count);
            Entities[id] = entity;
        }

        return entity;
    }

    public DiagramScene Render(RenderContext context, DiagramInfo info) => ErRenderer.Render(this, context, info);
}

internal static partial class ErParser
{
    private const string Name = @"(?:""[^""]+""|[\w\-]+(?:\[[^\]]*\])?)";

    private const string WordCardinality = @"one or zero|zero or one|one or more|one or many|many\(1\)|1\+|zero or more|zero or many|many\(0\)|0\+|only one|1";

    [GeneratedRegex(@"^(?<a>" + Name + @")\s*(?<left>\|o|\|\||\}o|\}\||" + WordCardinality + @")\s*(?<line>--|\.\.|optionally to|to)\s*(?<right>o\||\|\||o\{|\|\{|" + WordCardinality + @")\s*(?<b>" + Name + @")\s*:\s*(?<label>.*)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex RelationLine();

    [GeneratedRegex(@"^(?<id>" + Name + @")(?<classes>(?::::[\w\-]+)*)\s*(?<open>\{)?\s*(?<close>\})?$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityLine();

    [GeneratedRegex(@"^(?<type>[\w\-\[\]\(\),.]+)\s+(?<name>[\w\-\[\]\(\)*.]+)(?:\s+(?<keys>(?:PK|FK|UK)(?:\s*,\s*(?:PK|FK|UK))*))?(?:\s+""(?<comment>[^""]*)"")?$", RegexOptions.CultureInvariant)]
    private static partial Regex AttributeLine();

    public static ErModel Parse(Source source)
    {
        var model = new ErModel();
        ErEntity? body = null;
        foreach (var line in source.Lines)
        {
            var text = line.Trimmed;
            if (body is not null)
            {
                if (text == "}")
                {
                    body = null;
                    continue;
                }

                var attribute = AttributeLine().Match(text);
                if (!attribute.Success)
                {
                    throw Error(line, $"Invalid attribute '{Source.Truncate(text)}'; use 'type name [PK|FK|UK] [\"comment\"]'.");
                }

                CheckCount(body.Attributes.Count + 1, MermaidEngine.MaxElements, line.Number, "attributes in one entity");
                body.Attributes.Add(new ErAttribute(
                    attribute.Groups["type"].Value, attribute.Groups["name"].Value,
                    Regex.Replace(attribute.Groups["keys"].Value, @"\s+", ""), attribute.Groups["comment"].Value));
                continue;
            }

            var word = text.Split([' ', '\t'], 2)[0];
            var rest = text.Length > word.Length ? text[word.Length..].Trim() : "";
            switch (word)
            {
                case "direction":
                    model.Direction = FlowchartModel.ParseDirection(rest) ?? throw Error(line, $"Unknown direction '{Source.Truncate(rest)}'.");
                    continue;
                case "classDef":
                    var parts = rest.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        foreach (var id in parts[0].Split(','))
                        {
                            model.ClassDefs[id.Trim()] = Style.Parse(parts[1]);
                        }
                    }

                    continue;
                case "class":
                    var classParts = rest.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
                    if (classParts.Length == 2)
                    {
                        foreach (var id in classParts[0].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                        {
                            Declare(model, id).Classes.Add(classParts[1].Trim());
                        }
                    }

                    continue;
                case "style":
                    var styleParts = rest.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
                    if (styleParts.Length == 2)
                    {
                        foreach (var id in styleParts[0].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                        {
                            var entity = Declare(model, id);
                            entity.Style = (entity.Style ?? Style.None).Merge(Style.Parse(styleParts[1]));
                        }
                    }

                    continue;
            }

            if (RelationLine().Match(text) is { Success: true } relation)
            {
                CheckCount(model.Relations.Count + 1, MermaidEngine.MaxConnections, line.Number, "relationships");
                var a = Declare(model, relation.Groups["a"].Value);
                var b = Declare(model, relation.Groups["b"].Value);
                var lineKind = relation.Groups["line"].Value.ToLowerInvariant();
                model.Relations.Add(new ErRelation(
                    a.Id, b.Id, Cardinality(relation.Groups["left"].Value), Cardinality(relation.Groups["right"].Value),
                    lineKind is "--" or "to", Unquote(relation.Groups["label"].Value)));
                continue;
            }

            if (EntityLine().Match(text) is { Success: true } entityMatch)
            {
                var entity = Declare(model, entityMatch.Groups["id"].Value);
                foreach (var c in entityMatch.Groups["classes"].Value.Split(":::", StringSplitOptions.RemoveEmptyEntries))
                {
                    entity.Classes.Add(c);
                }

                if (entityMatch.Groups["open"].Success && !entityMatch.Groups["close"].Success)
                {
                    body = entity;
                }

                continue;
            }

            throw Error(line, $"Unrecognized statement '{Source.Truncate(text)}'.");
        }

        if (body is not null)
        {
            throw new MermaidParseException(MermaidErrorCode.UnclosedBlock, $"Entity '{Source.Truncate(body.Id)}' is not closed with '}}'.", source.Lines[^1].Number);
        }

        CheckCount(model.Entities.Count, MermaidEngine.MaxElements, source.HeaderLine, "entities");
        return model;
    }

    private static ErEntity Declare(ErModel model, string raw)
    {
        var text = raw.Trim();
        string? alias = null;
        var bracket = text.IndexOf('[');
        if (bracket > 0 && text.EndsWith(']'))
        {
            alias = Unquote(text[(bracket + 1)..^1]);
            text = text[..bracket];
        }

        var entity = model.Ensure(Unquote(text));
        if (alias is not null)
        {
            entity.Alias = alias;
        }

        return entity;
    }

    private static Marker Cardinality(string token) => token.ToLowerInvariant() switch
    {
        "|o" or "o|" or "one or zero" or "zero or one" => Marker.ZeroOrOne,
        "}|" or "|{" or "one or more" or "one or many" or "many(1)" or "1+" => Marker.OneOrMore,
        "}o" or "o{" or "zero or more" or "zero or many" or "many(0)" or "0+" => Marker.ZeroOrMore,
        _ => Marker.One
    };
}

internal static class ErRenderer
{
    private const double Padding = 15;
    private const double CellPadding = 8;

    private sealed record Box(MeasuredText Name, List<MeasuredText[]> Rows, double[] Columns, double Width, double HeaderHeight, double RowHeight, double Height);

    public static DiagramScene Render(ErModel model, RenderContext context, DiagramInfo info)
    {
        var theme = context.Theme;
        var config = context.Config;
        var graph = new LayeredGraph
        {
            Direction = model.Direction,
            NodeSpacing = config.Number("er", "nodeSpacing", 50),
            RankSpacing = config.Number("er", "rankSpacing", 70)
        };

        var minWidth = config.Number("er", "minEntityWidth", 100);
        var minHeight = config.Number("er", "minEntityHeight", 75);
        var boxes = new Dictionary<string, Box>(StringComparer.Ordinal);
        foreach (var entity in model.Entities.Values.OrderBy(e => e.Order))
        {
            var box = Measure(entity, context, minWidth, minHeight);
            boxes[entity.Id] = box;
            graph.AddNode(entity.Id, box.Width, box.Height);
        }

        var labels = new List<MeasuredText?>();
        foreach (var relation in model.Relations)
        {
            var label = relation.Label.Length == 0 ? null : context.Measure(relation.Label, 14);
            labels.Add(label);
            graph.AddEdge(relation.From, relation.To, label is null ? 0 : label.Width + 8, label?.Height ?? 0, 1);
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
            var visual = new EdgeVisual(line, 1, relation.Identifying ? null : [8, 8], relation.FromMarker, relation.ToMarker, CurveKind.Basis,
                labels[i], theme.Hex("relationLabelColor", theme.Hex("textColor")), theme.Hex("relationLabelBackground", theme.Hex("edgeLabelBackground")));
            EdgeDrawer.Draw(canvas, points, visual, routed.LabelCenter, background);
        }

        foreach (var entity in model.Entities.Values)
        {
            Draw(canvas, entity, boxes[entity.Id], layout.Nodes[entity.Id].Bounds, model, context);
        }

        canvas.Title(info.Title, theme.Hex("textColor"));
        return canvas.Finish(DiagramKind.EntityRelationship, info.Title, info.AccDescription, 8, null);
    }

    private static Box Measure(ErEntity entity, RenderContext context, double minWidth, double minHeight)
    {
        var name = context.Measure(entity.Alias ?? entity.Id);
        var rows = entity.Attributes.Select(a => new[]
        {
            context.MeasurePlain(a.Type, 14), context.MeasurePlain(a.Name, 14), context.MeasurePlain(a.Keys, 14), context.MeasurePlain(a.Comment, 14)
        }).ToList();
        if (rows.Count == 0)
        {
            var w = Math.Max(minWidth, name.Width + 2 * Padding);
            return new Box(name, rows, [], w, Math.Max(minHeight, name.Height + 2 * Padding), 0, Math.Max(minHeight, name.Height + 2 * Padding));
        }

        var columns = new double[4];
        for (var c = 0; c < 4; c++)
        {
            var widest = rows.Max(r => r[c].Lines.Count == 0 ? 0 : r[c].Width);
            columns[c] = widest > 0 ? widest + 2 * CellPadding : 0;
        }

        var rowHeight = rows.Max(r => r.Max(cell => cell.Height)) + CellPadding;
        var tableWidth = columns.Sum();
        var width = Math.Max(Math.Max(minWidth, name.Width + 2 * Padding), tableWidth);
        if (tableWidth < width)
        {
            var extra = (width - tableWidth) / columns.Count(c => c > 0);
            for (var c = 0; c < 4; c++)
            {
                if (columns[c] > 0)
                {
                    columns[c] += extra;
                }
            }
        }

        var header = name.Height + CellPadding * 2;
        return new Box(name, rows, columns, width, header, rowHeight, header + rows.Count * rowHeight);
    }

    private static void Draw(Canvas canvas, ErEntity entity, Box box, Rect r, ErModel model, RenderContext context)
    {
        var theme = context.Theme;
        var style = entity.Classes.Count == 0 ? model.ClassDefs.GetValueOrDefault("default") ?? Style.None : Style.None;
        foreach (var name in entity.Classes)
        {
            style = style.Merge(model.ClassDefs.GetValueOrDefault(name));
        }

        style = style.Merge(entity.Style);
        var fill = Style.Resolve(style.Fill, theme.Hex("mainBkg")) ?? "#00000000";
        var stroke = Style.Resolve(style.Stroke, theme.Hex("nodeBorder")) ?? "#00000000";
        var color = style.Color ?? theme.Hex("textColor");
        canvas.Rect(r.X, r.Y, r.Width, r.Height, fill, stroke, style.StrokeWidth ?? 1);
        if (box.Rows.Count == 0)
        {
            canvas.LabelCentered(box.Name, r.CenterX, r.CenterY, color);
            return;
        }

        canvas.LabelCentered(box.Name, r.CenterX, r.Y + box.HeaderHeight / 2, color);
        var y = r.Y + box.HeaderHeight;
        var odd = theme.Hex("attributeBackgroundColorOdd");
        var even = theme.Hex("attributeBackgroundColorEven");
        for (var i = 0; i < box.Rows.Count; i++)
        {
            canvas.Rect(r.X, y, r.Width, box.RowHeight, i % 2 == 0 ? odd : even, stroke, 0.5);
            var x = r.X;
            for (var c = 0; c < 4; c++)
            {
                if (box.Columns[c] <= 0)
                {
                    continue;
                }

                if (x > r.X)
                {
                    canvas.Line(x, y, x, y + box.RowHeight, stroke, 0.5);
                }

                var cell = box.Rows[i][c];
                canvas.Label(cell, x + CellPadding, y + (box.RowHeight - cell.Height) / 2, TextAnchor.Start, theme.Hex("textColor"));
                x += box.Columns[c];
            }

            y += box.RowHeight;
        }

        canvas.Rect(r.X, r.Y, r.Width, r.Height, null, stroke, style.StrokeWidth ?? 1);
    }
}
