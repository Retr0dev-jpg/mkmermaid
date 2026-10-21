using System.Text.RegularExpressions;
using MkMermaid.Parsing;
using MkMermaid.Rendering;
using MkMermaid.Scene;
using MkMermaid.Text;
using static MkMermaid.Parsing.ParseHelpers;

namespace MkMermaid.Diagrams.Kanban;

internal sealed record KanbanCard(string Id, string Label, string? Ticket, string? Assigned, string? Priority);

internal sealed class KanbanColumn(string id, string label)
{
    public string Id { get; } = id;

    public string Label { get; } = label;

    public List<KanbanCard> Cards { get; } = [];
}

internal sealed class KanbanModel : IDiagramModel
{
    public string? Title => null;

    public List<KanbanColumn> Columns { get; } = [];

    public DiagramScene Render(RenderContext context, DiagramInfo info) => KanbanRenderer.Render(this, context, info);
}

internal static partial class KanbanParser
{
    // Like Mermaid's NODE_ID token, a bare id may contain spaces ("In progress").
    [GeneratedRegex(@"^(?<id>[^\[\]()@{}]+?)?\s*(?:\[(?<label>.*)\])?\s*(?:@\{(?<meta>.*)\})?$", RegexOptions.CultureInvariant)]
    private static partial Regex Item();

    public static KanbanModel Parse(Source source)
    {
        var model = new KanbanModel();
        int? columnIndent = null;
        var count = 0;
        foreach (var line in source.Lines)
        {
            var match = Item().Match(line.Trimmed);
            if (!match.Success || (!match.Groups["id"].Success && !match.Groups["label"].Success))
            {
                throw Error(line, $"Unrecognized item '{Source.Truncate(line.Trimmed)}'; write id[Label] or just a label.");
            }

            CheckCount(++count, MermaidEngine.MaxElements, line.Number, "items");
            var id = match.Groups["id"].Success ? match.Groups["id"].Value : $"item{count}";
            var label = match.Groups["label"].Success ? Unquote(match.Groups["label"].Value) : id;
            columnIndent ??= line.Indent;
            if (line.Indent <= columnIndent)
            {
                model.Columns.Add(new KanbanColumn(id, label));
                continue;
            }

            if (model.Columns.Count == 0)
            {
                throw Error(line, "A card must be indented under a column.");
            }

            var meta = match.Groups["meta"].Success ? InlineMap(match.Groups["meta"].Value) : new Dictionary<string, string>();
            model.Columns[^1].Cards.Add(new KanbanCard(id, label, meta.GetValueOrDefault("ticket"), meta.GetValueOrDefault("assigned"), meta.GetValueOrDefault("priority")));
        }

        return model;
    }
}

internal static class KanbanRenderer
{
    public static DiagramScene Render(KanbanModel model, RenderContext context, DiagramInfo info)
    {
        var theme = context.Theme;
        var config = context.Config;
        var width = config.Number("kanban", "sectionWidth", 200);
        var padding = config.Number("kanban", "padding", 8);
        var canvas = context.CreateCanvas();
        var cardWidth = width - 2 * padding;
        var meta = context.FontSize * 0.8;

        // Cards first, so every column can share the height of the tallest one.
        var layouts = model.Columns.Select(column =>
        {
            var header = context.Measure(column.Label, null, cardWidth, forceWrap: true);
            var cards = column.Cards.Select(card =>
            {
                var label = context.Measure(card.Label, null, cardWidth - 2 * padding, forceWrap: true);
                var hasMeta = card.Ticket is not null || card.Assigned is not null;
                return (Card: card, Label: label, Height: label.Height + 2 * padding + (hasMeta ? meta + 6 : 0));
            }).ToList();
            return (Column: column, Header: header, Cards: cards);
        }).ToList();
        var headerHeight = layouts.Count == 0 ? 0 : layouts.Max(l => l.Header.Height) + 2 * padding;
        var columnHeight = layouts.Count == 0 ? 0 : layouts.Max(l => headerHeight + l.Cards.Sum(c => c.Height + padding) + padding);

        for (var i = 0; i < layouts.Count; i++)
        {
            var (column, header, cards) = layouts[i];
            var color = (i % 11 + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var x = i * (width + padding * 1.5);
            canvas.Rect(x, 0, width, columnHeight, theme.Hex("cScale" + color), null, 0, 5);
            canvas.Label(header, x + width / 2, padding, TextAnchor.Middle, theme.Hex("cScaleLabel" + color));
            var y = headerHeight;
            foreach (var (card, label, height) in cards)
            {
                var cx = x + padding;
                canvas.Rect(cx, y, cardWidth, height, theme.Hex("background", "#FFFFFF"), theme.Hex("nodeBorder"), 1, 5);
                if (PriorityColor(card.Priority) is { } priority)
                {
                    canvas.Line(cx + 2, y + 2, cx + 2, y + height - 2, priority, 4);
                }

                canvas.Label(label, cx + padding, y + padding, TextAnchor.Start, theme.Hex("textColor"));
                var metaY = y + padding + label.Height + meta + 2;
                if (card.Ticket is not null)
                {
                    canvas.Text(card.Ticket, cx + padding, metaY, meta, TextAnchor.Start, theme.Hex("textColor"));
                }

                if (card.Assigned is not null)
                {
                    canvas.Text(card.Assigned, cx + cardWidth - padding, metaY, meta, TextAnchor.End, theme.Hex("textColor"));
                }

                y += height + padding;
            }
        }

        canvas.Title(info.Title, theme.Hex("textColor"));
        return canvas.Finish(DiagramKind.Kanban, info.Title, info.AccDescription, 10, null);
    }

    private static string? PriorityColor(string? priority) => priority?.Trim().ToLowerInvariant() switch
    {
        "very high" => "#FF0000",
        "high" => "#FFA500",
        "low" => "#0000FF",
        "very low" => "#ADD8E6",
        _ => null
    };
}
