using System.Globalization;
using MkMermaid.Parsing;
using MkMermaid.Rendering;
using MkMermaid.Scene;
using static MkMermaid.Parsing.ParseHelpers;

namespace MkMermaid.Diagrams.Journey;

internal sealed record JourneyTask(string Name, int Score, IReadOnlyList<string> Actors, int Section);

internal sealed class JourneyModel : IDiagramModel
{
    public string? Title { get; set; }

    public List<string> Sections { get; } = [];

    public List<JourneyTask> Tasks { get; } = [];

    public DiagramScene Render(RenderContext context, DiagramInfo info) => JourneyRenderer.Render(this, context, info);
}

internal static class JourneyParser
{
    public static JourneyModel Parse(Source source)
    {
        var model = new JourneyModel();
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
                continue;
            }

            var parts = text.Split(':');
            if (parts.Length < 2)
            {
                throw Error(line, $"Unrecognized statement '{Source.Truncate(text)}'; a task is written 'Task: score: actors'.");
            }

            if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var score))
            {
                throw new MermaidParseException(MermaidErrorCode.InvalidValue, $"The score of '{Source.Truncate(parts[0].Trim())}' must be a whole number.", line.Number);
            }

            CheckCount(model.Tasks.Count + 1, MermaidEngine.MaxElements, line.Number, "tasks");
            if (model.Sections.Count == 0)
            {
                model.Sections.Add("");
            }

            var actors = parts.Length > 2
                ? string.Join(":", parts.Skip(2)).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                : [];
            model.Tasks.Add(new JourneyTask(parts[0].Trim(), Math.Clamp(score, 0, 5), actors, model.Sections.Count - 1));
        }

        return model;
    }
}

internal static class JourneyRenderer
{
    private static readonly string[] ActorColors = ["#8FBC8F", "#7CFC00", "#00FFFF", "#20B2AA", "#B0E0E6", "#FFFFE0"];

    public static DiagramScene Render(JourneyModel model, RenderContext context, DiagramInfo info)
    {
        var theme = context.Theme;
        const double taskWidth = 150;
        const double taskHeight = 50;
        const double taskGap = 50;
        const double sectionTop = 0;
        const double taskTop = 60;
        const double faceTop = taskTop + taskHeight + 40;
        var canvas = context.CreateCanvas();
        var actors = model.Tasks.SelectMany(t => t.Actors).Distinct(StringComparer.Ordinal).ToList();
        var actorColor = actors.Select((a, i) => (a, ActorColors[i % ActorColors.Length])).ToDictionary(p => p.a, p => p.Item2, StringComparer.Ordinal);
        var legendWidth = actors.Count == 0 ? 0 : actors.Max(a => context.TextWidth(a, 14)) + 40;
        var left = legendWidth + 20;
        var bottom = faceTop + 5 * 30 + 30;
        for (var i = 0; i < actors.Count; i++)
        {
            var y = taskTop + i * 22;
            canvas.Circle(10, y, 7, actorColor[actors[i]], "#000000");
            canvas.Text(actors[i], 24, y + 5, 14, TextAnchor.Start, theme.Hex("textColor"));
        }

        var x = left;
        for (var s = 0; s < model.Sections.Count; s++)
        {
            var tasks = model.Tasks.Where(t => t.Section == s).ToList();
            if (tasks.Count == 0)
            {
                continue;
            }

            var fill = theme.Hex("fillType" + s % 8);
            var width = tasks.Count * taskWidth + (tasks.Count - 1) * taskGap;
            canvas.Rect(x, sectionTop, width, taskHeight, fill, theme.C("fillType" + s % 8).Darken(10).ToString(), 1, 3);
            canvas.LabelCentered(context.Measure(model.Sections[s], 14, width - 10, forceWrap: true), x + width / 2, sectionTop + taskHeight / 2, theme.Hex("textColor"));
            foreach (var task in tasks)
            {
                var cx = x + taskWidth / 2;
                canvas.Line(cx, taskTop + taskHeight, cx, bottom, theme.Hex("textColor"), 1, [4, 2]);
                canvas.Rect(x, taskTop, taskWidth, taskHeight, fill, theme.C("fillType" + s % 8).Darken(10).ToString(), 1, 3);
                canvas.LabelCentered(context.Measure(task.Name, 14, taskWidth - 10, forceWrap: true), cx, taskTop + taskHeight / 2, theme.Hex("textColor"));
                var actorX = x + 10;
                foreach (var actor in task.Actors)
                {
                    canvas.Circle(actorX, taskTop, 7, actorColor[actor], "#666666");
                    actorX += 10;
                }

                DrawFace(canvas, cx, faceTop + (5 - task.Score) * 30, task.Score);
                x += taskWidth + taskGap;
            }
        }

        canvas.Line(left - 10, bottom, Math.Max(left, x - taskGap + 10), bottom, theme.Hex("textColor"), 2);
        Markers.Draw(canvas, Marker.Arrow, new Layout.Point(Math.Max(left, x - taskGap + 20), bottom), 1, 0, theme.Hex("textColor"), theme.Hex("background", "#FFFFFF"), 2);
        canvas.Title(info.Title, theme.Hex("titleColor", theme.Hex("textColor")));
        return canvas.Finish(DiagramKind.Journey, info.Title, info.AccDescription, 10, null);
    }

    private static void DrawFace(Canvas canvas, double cx, double cy, int score)
    {
        const double r = 15;
        canvas.Circle(cx, cy, r, "#FFF8DC", "#999999", 2);
        canvas.Circle(cx - r / 3, cy - r / 3, 1.5, "#666666", "#666666");
        canvas.Circle(cx + r / 3, cy - r / 3, 1.5, "#666666", "#666666");
        if (score > 3)
        {
            canvas.Shape(new PathGeometry().MoveTo(cx - r / 2, cy + r / 5).ArcTo(cx, cy + r / 5, r / 2, r / 2.5, Math.PI, -Math.PI), null, "#666666", 2);
        }
        else if (score < 3)
        {
            canvas.Shape(new PathGeometry().MoveTo(cx - r / 2, cy + r / 2).ArcTo(cx, cy + r / 2, r / 2, r / 3, Math.PI, Math.PI), null, "#666666", 2);
        }
        else
        {
            canvas.Line(cx - r / 2, cy + r / 3, cx + r / 2, cy + r / 3, "#666666", 2);
        }
    }
}
