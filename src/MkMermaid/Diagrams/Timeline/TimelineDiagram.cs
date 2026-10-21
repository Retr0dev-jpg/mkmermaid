using MkMermaid.Parsing;
using MkMermaid.Rendering;
using MkMermaid.Scene;
using MkMermaid.Text;
using static MkMermaid.Parsing.ParseHelpers;

namespace MkMermaid.Diagrams.Timeline;

internal sealed class TimelinePeriod(string label, int section)
{
    public string Label { get; } = label;

    public int Section { get; } = section;

    public List<string> Events { get; } = [];
}

internal sealed class TimelineModel : IDiagramModel
{
    public string? Title { get; set; }

    public List<string> Sections { get; } = [];

    public List<TimelinePeriod> Periods { get; } = [];

    public DiagramScene Render(RenderContext context, DiagramInfo info) => TimelineRenderer.Render(this, context, info);
}

internal static class TimelineParser
{
    public static TimelineModel Parse(Source source)
    {
        var model = new TimelineModel();
        TimelinePeriod? current = null;
        foreach (var line in source.Lines)
        {
            var text = line.Trimmed;
            if (StartsWithWord(text, "title"))
            {
                model.Title = text[5..].Trim();
                continue;
            }

            if (StartsWithWord(text, "section"))
            {
                model.Sections.Add(text[7..].Trim());
                current = null;
                continue;
            }

            // "period : event : event" or a continuation line ": event" for the previous period.
            var parts = SplitEvents(text);
            if (text.StartsWith(':'))
            {
                if (current is null)
                {
                    throw Error(line, "An event line must follow a time period.");
                }
            }
            else
            {
                CheckCount(model.Periods.Count + 1, MermaidEngine.MaxElements, line.Number, "time periods");
                current = new TimelinePeriod(parts[0], model.Sections.Count - 1);
                model.Periods.Add(current);
            }

            foreach (var item in parts.Skip(1))
            {
                if (item.Length > 0)
                {
                    current.Events.Add(item);
                }
            }
        }

        return model;
    }

    /// <summary>Splits on colons followed by a space or the end, so times such as 10:30 survive.</summary>
    private static List<string> SplitEvents(string text)
    {
        var parts = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == ':' && (i + 1 == text.Length || char.IsWhiteSpace(text[i + 1]) || i == 0))
            {
                parts.Add(text[start..i].Trim());
                start = i + 1;
            }
        }

        parts.Add(text[start..].Trim());
        return parts;
    }
}

internal static class TimelineRenderer
{
    private const double BoxWidth = 150;
    private const double Gap = 50;
    private const double Padding = 10;

    public static DiagramScene Render(TimelineModel model, RenderContext context, DiagramInfo info)
    {
        var theme = context.Theme;
        var disableMulticolor = context.Config.Flag("timeline", "disableMulticolor", false);
        var canvas = context.CreateCanvas();
        MeasuredText Measure(string text) => context.Measure(text, null, BoxWidth - 2 * Padding, forceWrap: true);
        double BoxHeight(MeasuredText text) => Math.Max(text.Height + 2 * Padding, 40);
        int Color(int index) => disableMulticolor ? 0 : index % 11 + 1;

        var periodLabels = model.Periods.Select(p => Measure(p.Label)).ToList();
        var periodHeight = periodLabels.Count == 0 ? 40 : periodLabels.Max(BoxHeight);
        var sectionLabels = model.Sections.Select(Measure).ToList();
        var sectionHeight = sectionLabels.Count == 0 ? 0 : sectionLabels.Max(BoxHeight);
        var periodTop = sectionHeight > 0 ? sectionHeight + 20 : 0;
        var lineY = periodTop + periodHeight + 40;
        var eventsTop = lineY + 40;

        // Event columns first, so the dashed guides can run to the deepest one.
        var columns = new List<(double X, List<(MeasuredText Label, double Y, double H)> Events)>();
        var deepest = eventsTop;
        for (var i = 0; i < model.Periods.Count; i++)
        {
            var x = i * (BoxWidth + Gap);
            var y = eventsTop;
            var events = new List<(MeasuredText, double, double)>();
            foreach (var text in model.Periods[i].Events)
            {
                var label = Measure(text);
                var h = BoxHeight(label);
                events.Add((label, y, h));
                y += h + 10;
            }

            deepest = Math.Max(deepest, y);
            columns.Add((x, events));
        }

        // Sections span their periods; without sections every period gets its own color.
        for (var s = 0; s < model.Sections.Count; s++)
        {
            var members = Enumerable.Range(0, model.Periods.Count).Where(i => model.Periods[i].Section == s).ToList();
            if (members.Count == 0)
            {
                continue;
            }

            var x = members[0] * (BoxWidth + Gap);
            var w = (members[^1] - members[0]) * (BoxWidth + Gap) + BoxWidth;
            Node(canvas, theme, x, 0, w, sectionHeight, sectionLabels[s], Color(s));
        }

        for (var i = 0; i < model.Periods.Count; i++)
        {
            var period = model.Periods[i];
            var color = Color(period.Section >= 0 ? period.Section : i);
            var x = columns[i].X;
            canvas.Line(x + BoxWidth / 2, periodTop + periodHeight, x + BoxWidth / 2, deepest, theme.Hex("lineColor", theme.Hex("textColor")), 2, [5, 5]);
            Node(canvas, theme, x, periodTop, BoxWidth, periodHeight, periodLabels[i], color);
            foreach (var (label, y, h) in columns[i].Events)
            {
                Node(canvas, theme, x, y, BoxWidth, h, label, color);
            }
        }

        if (model.Periods.Count > 0)
        {
            var end = model.Periods.Count * (BoxWidth + Gap);
            var stroke = theme.Hex("textColor");
            canvas.Line(-Gap / 2, lineY, end - 8, lineY, stroke, 4);
            canvas.Shape(PathGeometry.Polygon([(end, lineY), (end - 14, lineY - 8), (end - 14, lineY + 8)]), stroke, null, 0);
        }

        if (!string.IsNullOrWhiteSpace(info.Title))
        {
            var title = context.MeasurePlain(info.Title, 32, bold: true);
            canvas.Label(title, -Gap / 2, -title.Height - 20, TextAnchor.Start, theme.Hex("titleColor", theme.Hex("textColor")), bold: true);
        }

        return canvas.Finish(DiagramKind.Timeline, info.Title, info.AccDescription, 10, null);
    }

    /// <summary>Mermaid's timeline node: rounded top corners and a contrasting line along the bottom edge.</summary>
    private static void Node(Canvas canvas, Theming.Theme theme, double x, double y, double w, double h, MeasuredText label, int color)
    {
        const double r = 5;
        var path = new PathGeometry().MoveTo(x, y + h).LineTo(x, y + r);
        path.ArcTo(x + r, y + r, r, r, Math.PI, Math.PI / 2);
        path.LineTo(x + w - r, y);
        path.ArcTo(x + w - r, y + r, r, r, -Math.PI / 2, Math.PI / 2);
        path.LineTo(x + w, y + h).Close();
        canvas.Shape(path, theme.Hex("cScale" + color), null, 0);
        canvas.Line(x, y + h, x + w, y + h, theme.Hex("cScaleInv" + color), 3);
        canvas.Label(label, x + w / 2, y + (h - label.Height) / 2, TextAnchor.Middle, theme.Hex("cScaleLabel" + color));
    }
}
