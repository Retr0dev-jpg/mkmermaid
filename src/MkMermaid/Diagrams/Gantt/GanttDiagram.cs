using System.Globalization;
using System.Text.RegularExpressions;
using MkMermaid.Parsing;
using MkMermaid.Rendering;
using MkMermaid.Scene;
using MkMermaid.Text;
using static MkMermaid.Parsing.ParseHelpers;

namespace MkMermaid.Diagrams.Gantt;

internal sealed class GanttTask
{
    public required string Name { get; init; }

    public required int Section { get; init; }

    public required int Line { get; init; }

    public string? Id { get; init; }

    public bool Done { get; init; }

    public bool Active { get; init; }

    public bool Critical { get; init; }

    public bool Milestone { get; init; }

    public bool Vertical { get; init; }

    public DateTime Start { get; set; }

    public DateTime End { get; set; }
}

internal sealed class GanttModel : IDiagramModel
{
    public string? Title { get; set; }

    public List<string> Sections { get; } = [];

    public List<GanttTask> Tasks { get; } = [];

    public string AxisFormat { get; set; } = "%Y-%m-%d";

    public string? TickInterval { get; set; }

    public bool Compact { get; set; }

    public bool TopAxis { get; set; }

    public Func<DateTime, bool> Excluded { get; set; } = _ => false;

    public bool HasExclusions { get; set; }

    public DayOfWeek WeekStart { get; set; } = DayOfWeek.Sunday;

    public DiagramScene Render(RenderContext context, DiagramInfo info) => GanttRenderer.Render(this, context, info);
}

internal static partial class GanttParser
{
    private const int MaxExcludedDays = 10_000;

    private sealed record RawTask(int Line, string Name, int Section, string? Id, string? StartSpec, string EndSpec, int Previous, HashSet<string> Tags);

    [GeneratedRegex(@"^(?<count>[1-9][0-9]*)(?<unit>millisecond|second|minute|hour|day|week|month)$", RegexOptions.CultureInvariant)]
    private static partial Regex Interval();

    public static GanttModel Parse(Source source)
    {
        var model = new GanttModel();
        var dateFormat = "YYYY-MM-DD";
        var inclusiveEnd = false;
        var excludes = new List<string>();
        var weekend = DayOfWeek.Saturday;
        var raw = new List<RawTask>();
        model.AxisFormat = source.Config.Text("gantt", "axisFormat") ?? model.AxisFormat;
        model.Compact = string.Equals(source.Config.Text("gantt", "displayMode"), "compact", StringComparison.OrdinalIgnoreCase);
        model.TopAxis = source.Config.Flag("gantt", "topAxis", false);
        foreach (var line in source.Lines)
        {
            var text = line.Trimmed;
            var word = text.Split([' ', '\t', ':'], 2)[0];
            var rest = text.Length > word.Length ? text[word.Length..].Trim() : "";
            switch (word)
            {
                case "title":
                    model.Title = rest;
                    continue;
                case "dateFormat":
                    dateFormat = rest;
                    continue;
                case "axisFormat":
                    model.AxisFormat = rest;
                    continue;
                case "tickInterval":
                    if (!Interval().IsMatch(rest))
                    {
                        throw new MermaidParseException(MermaidErrorCode.InvalidValue, $"Invalid tickInterval '{Source.Truncate(rest)}'.", line.Number);
                    }

                    model.TickInterval = rest;
                    continue;
                case "excludes":
                    excludes.AddRange(rest.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries));
                    continue;
                case "includes":
                    continue;
                case "weekend":
                    weekend = rest.Equals("friday", StringComparison.OrdinalIgnoreCase) ? DayOfWeek.Friday : DayOfWeek.Saturday;
                    continue;
                case "weekday":
                    if (Enum.TryParse<DayOfWeek>(rest, true, out var weekday))
                    {
                        model.WeekStart = weekday;
                    }

                    continue;
                case "inclusiveEndDates":
                    inclusiveEnd = true;
                    continue;
                case "topAxis":
                    model.TopAxis = true;
                    continue;
                case "todayMarker" or "click" or "accTitle" or "accDescr":
                    continue;
                case "displayMode":
                    model.Compact = rest.Equals("compact", StringComparison.OrdinalIgnoreCase);
                    continue;
                case "section":
                    model.Sections.Add(rest);
                    continue;
            }

            var colon = text.IndexOf(':');
            if (colon <= 0)
            {
                throw Error(line, $"Unrecognized statement '{Source.Truncate(text)}'; a task is written 'Name : metadata'.");
            }

            CheckCount(raw.Count + 1, MermaidEngine.MaxElements, line.Number, "tasks");
            if (model.Sections.Count == 0)
            {
                model.Sections.Add("");
            }

            var parts = text[(colon + 1)..].Split(',', StringSplitOptions.TrimEntries).Where(p => p.Length > 0).ToList();
            var tags = new HashSet<string>(StringComparer.Ordinal);
            while (parts.Count > 0 && parts[0] is "done" or "active" or "crit" or "milestone" or "vert")
            {
                tags.Add(parts[0]);
                parts.RemoveAt(0);
            }

            var (id, start, end) = parts.Count switch
            {
                1 => ((string?)null, (string?)null, parts[0]),
                2 => (null, parts[0], parts[1]),
                3 => (parts[0], parts[1], parts[2]),
                _ => throw Error(line, "A task needs an end or duration, optionally preceded by an id and a start.")
            };
            raw.Add(new RawTask(line.Number, text[..colon].Trim(), model.Sections.Count - 1, id, start, end, raw.Count - 1, tags));
        }

        model.Excluded = BuildExclusions(excludes, dateFormat, weekend);
        model.HasExclusions = excludes.Count > 0;
        Resolve(model, raw, dateFormat, inclusiveEnd);
        return model;
    }

    private static Func<DateTime, bool> BuildExclusions(List<string> excludes, string dateFormat, DayOfWeek weekend)
    {
        var days = new HashSet<DayOfWeek>();
        var dates = new HashSet<DateTime>();
        foreach (var item in excludes)
        {
            if (item.Equals("weekends", StringComparison.OrdinalIgnoreCase))
            {
                days.Add(weekend);
                days.Add(weekend == DayOfWeek.Friday ? DayOfWeek.Saturday : DayOfWeek.Sunday);
            }
            else if (Enum.TryParse<DayOfWeek>(item, true, out var day) && !int.TryParse(item, out _))
            {
                days.Add(day);
            }
            else if (GanttDates.TryParse(item, dateFormat, out var date) || DateTime.TryParse(item, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                dates.Add(date.Date);
            }
        }

        return date => days.Contains(date.DayOfWeek) || dates.Contains(date.Date);
    }

    private static void Resolve(GanttModel model, List<RawTask> raw, string dateFormat, bool inclusiveEnd)
    {
        var resolved = new GanttTask?[raw.Count];
        var byId = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < raw.Count; i++)
        {
            if (raw[i].Id is { } id)
            {
                byId.TryAdd(id, i);
            }
        }

        var hasTime = dateFormat.Contains('H', StringComparison.Ordinal) || dateFormat.Contains('h', StringComparison.Ordinal) || dateFormat.Contains('m', StringComparison.Ordinal);
        for (var pass = 0; pass <= raw.Count; pass++)
        {
            var progress = false;
            for (var i = 0; i < raw.Count; i++)
            {
                if (resolved[i] is not null)
                {
                    continue;
                }

                var task = raw[i];
                if (!TryStart(task, raw, resolved, byId, dateFormat, out var start))
                {
                    continue;
                }

                DateTime end;
                var manualEnd = false;
                if (task.EndSpec.StartsWith("until ", StringComparison.Ordinal))
                {
                    var refs = task.EndSpec[6..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var targets = refs.Select(r => byId.TryGetValue(r, out var k) ? k : throw Unknown(task, r)).ToList();
                    if (targets.Any(k => resolved[k] is null))
                    {
                        continue;
                    }

                    end = targets.Min(k => resolved[k]!.Start);
                    manualEnd = true;
                }
                else if (GanttDates.TryParse(task.EndSpec, dateFormat, out var date))
                {
                    end = inclusiveEnd && !hasTime ? date.AddDays(1) : date;
                    manualEnd = true;
                }
                else if (!GanttDates.TryAddDuration(start, task.EndSpec, out end))
                {
                    throw new MermaidParseException(MermaidErrorCode.InvalidValue, $"Invalid end date or duration '{Source.Truncate(task.EndSpec)}' for '{Source.Truncate(task.Name)}'.", task.Line);
                }

                if (!manualEnd && model.HasExclusions)
                {
                    var guard = 0;
                    for (var d = start.Date; d < end && guard < MaxExcludedDays; d = d.AddDays(1), guard++)
                    {
                        if (model.Excluded(d))
                        {
                            end = end.AddDays(1);
                        }
                    }
                }

                resolved[i] = new GanttTask
                {
                    Name = task.Name,
                    Section = task.Section,
                    Line = task.Line,
                    Id = task.Id,
                    Done = task.Tags.Contains("done"),
                    Active = task.Tags.Contains("active"),
                    Critical = task.Tags.Contains("crit"),
                    Milestone = task.Tags.Contains("milestone"),
                    Vertical = task.Tags.Contains("vert"),
                    Start = start,
                    End = end < start ? start : end
                };
                progress = true;
            }

            if (!progress)
            {
                break;
            }
        }

        var missing = Array.FindIndex(resolved, t => t is null);
        if (missing >= 0)
        {
            throw new MermaidParseException(MermaidErrorCode.UnknownReference, $"The dates of '{Source.Truncate(raw[missing].Name)}' depend on each other in a cycle.", raw[missing].Line);
        }

        model.Tasks.AddRange(resolved!);
    }

    private static bool TryStart(RawTask task, List<RawTask> raw, GanttTask?[] resolved, Dictionary<string, int> byId, string dateFormat, out DateTime start)
    {
        start = default;
        if (task.StartSpec is null)
        {
            if (task.Previous < 0)
            {
                // Mermaid starts a first task without a start date today; a fixed date keeps output deterministic.
                start = new DateTime(2000, 1, 1);
                return true;
            }

            if (resolved[task.Previous] is not { } previous)
            {
                return false;
            }

            start = previous.End;
            return true;
        }

        if (task.StartSpec.StartsWith("after ", StringComparison.Ordinal))
        {
            var refs = task.StartSpec[6..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var targets = refs.Select(r => byId.TryGetValue(r, out var k) ? k : throw Unknown(task, r)).ToList();
            if (targets.Count == 0)
            {
                throw new MermaidParseException(MermaidErrorCode.Syntax, "'after' needs at least one task id.", task.Line);
            }

            if (targets.Any(k => resolved[k] is null))
            {
                return false;
            }

            start = targets.Max(k => resolved[k]!.End);
            return true;
        }

        if (GanttDates.TryParse(task.StartSpec, dateFormat, out start) ||
            DateTime.TryParse(task.StartSpec, CultureInfo.InvariantCulture, DateTimeStyles.None, out start))
        {
            return true;
        }

        throw new MermaidParseException(MermaidErrorCode.InvalidValue, $"Invalid start date '{Source.Truncate(task.StartSpec)}' for '{Source.Truncate(task.Name)}' (dateFormat is not matched).", task.Line);
    }

    private static MermaidParseException Unknown(RawTask task, string id) =>
        new(MermaidErrorCode.UnknownReference, $"Task '{Source.Truncate(task.Name)}' refers to the unknown task id '{Source.Truncate(id)}'.", task.Line);
}

internal static class GanttRenderer
{
    private static readonly (TimeSpan Span, int Months)[] TickSteps =
    [
        (TimeSpan.FromSeconds(1), 0), (TimeSpan.FromSeconds(5), 0), (TimeSpan.FromSeconds(15), 0), (TimeSpan.FromSeconds(30), 0),
        (TimeSpan.FromMinutes(1), 0), (TimeSpan.FromMinutes(5), 0), (TimeSpan.FromMinutes(15), 0), (TimeSpan.FromMinutes(30), 0),
        (TimeSpan.FromHours(1), 0), (TimeSpan.FromHours(3), 0), (TimeSpan.FromHours(6), 0), (TimeSpan.FromHours(12), 0),
        (TimeSpan.FromDays(1), 0), (TimeSpan.FromDays(2), 0), (TimeSpan.FromDays(7), 0),
        (TimeSpan.Zero, 1), (TimeSpan.Zero, 3), (TimeSpan.Zero, 12), (TimeSpan.Zero, 60), (TimeSpan.Zero, 120), (TimeSpan.Zero, 600)
    ];

    public static DiagramScene Render(GanttModel model, RenderContext context, DiagramInfo info)
    {
        var theme = context.Theme;
        var config = context.Config;
        var barHeight = config.Number("gantt", "barHeight", 20);
        var barGap = config.Number("gantt", "barGap", 4);
        var fontSize = config.Number("gantt", "fontSize", 11);
        var sectionFontSize = config.Number("gantt", "sectionFontSize", 11);
        var styles = Math.Max(1, (int)config.Number("gantt", "numberSectionStyles", 4));
        var totalWidth = Math.Clamp(config.Number("gantt", "useWidth", 900), 300, 4000);
        var canvas = context.CreateCanvas();
        if (model.Tasks.Count == 0)
        {
            canvas.Title(info.Title, theme.Hex("titleColor", theme.Hex("textColor")));
            return canvas.Finish(DiagramKind.Gantt, info.Title, info.AccDescription, 8, null);
        }

        var sectionLabels = model.Sections.Select(s => context.Measure(s, sectionFontSize, 150, forceWrap: true)).ToList();
        var left = Math.Max(config.Number("gantt", "leftPadding", 75), sectionLabels.Max(l => l.Width) + 20);
        var right = config.Number("gantt", "rightPadding", 75);
        var chartWidth = Math.Max(100, totalWidth - left - right);
        var min = model.Tasks.Min(t => t.Start);
        var max = model.Tasks.Max(t => t.End);
        if (max <= min)
        {
            max = min.AddDays(1);
        }

        var span = (max - min).TotalMilliseconds;
        double X(DateTime d) => left + (d - min).TotalMilliseconds / span * chartWidth;

        var rows = AssignRows(model);
        var rowHeight = barHeight + barGap;
        var rowCount = rows.Count == 0 ? 0 : rows.Max() + 1;
        var top = model.TopAxis ? 30 : 0;
        var chartBottom = top + rowCount * rowHeight;

        // Section bands.
        for (var s = 0; s < model.Sections.Count; s++)
        {
            var members = Enumerable.Range(0, model.Tasks.Count).Where(i => model.Tasks[i].Section == s && !model.Tasks[i].Vertical).ToList();
            if (members.Count == 0)
            {
                continue;
            }

            var first = members.Min(i => rows[i]);
            var last = members.Max(i => rows[i]);
            var color = (s % styles) switch
            {
                0 => theme.Hex("sectionBkgColor"),
                1 => theme.Hex("altSectionBkgColor"),
                2 => theme.Hex("sectionBkgColor2"),
                _ => theme.Hex("altSectionBkgColor")
            };
            var y = top + first * rowHeight;
            var height = (last - first + 1) * rowHeight;
            canvas.Rect(0, y, left + chartWidth + right / 2, height, color, null, 0, opacity: 0.2);
            canvas.Label(sectionLabels[s], 10, y + (height - sectionLabels[s].Height) / 2, TextAnchor.Start, theme.Hex("titleColor", theme.Hex("textColor")));
        }

        // Excluded days.
        if (model.HasExclusions && (max - min).TotalDays <= 3660)
        {
            for (var d = min.Date; d < max; d = d.AddDays(1))
            {
                if (model.Excluded(d))
                {
                    var x0 = X(d < min ? min : d);
                    var x1 = X(d.AddDays(1) > max ? max : d.AddDays(1));
                    canvas.Rect(x0, top, x1 - x0, chartBottom - top, theme.Hex("excludeBkgColor"), null, 0);
                }
            }
        }

        // Grid and axis.
        var grid = theme.Hex("gridColor", "lightgrey");
        var axisY = model.TopAxis ? top : chartBottom + 4;
        foreach (var tick in Ticks(model, min, max, chartWidth))
        {
            var x = X(tick);
            canvas.Line(x, top, x, chartBottom, grid, 1);
            var label = GanttDates.Format(tick, model.AxisFormat);
            canvas.Text(label, x, model.TopAxis ? top - 8 : axisY + 14, 10, TextAnchor.Middle, theme.Hex("textColor"));
        }

        // Tasks.
        for (var i = 0; i < model.Tasks.Count; i++)
        {
            var task = model.Tasks[i];
            var x0 = X(task.Start);
            var x1 = X(task.End);
            var y = top + rows[i] * rowHeight + barGap / 2;
            if (task.Vertical)
            {
                canvas.Line(x0, top, x0, chartBottom, theme.Hex("critBorderColor"), 2);
                canvas.Text(task.Name, x0, top - 4, fontSize, TextAnchor.Middle, theme.Hex("textColor"));
                continue;
            }

            var (fill, stroke) = task switch
            {
                { Critical: true, Done: true } => (theme.Hex("doneTaskBkgColor"), theme.Hex("critBorderColor")),
                { Critical: true, Active: true } => (theme.Hex("activeTaskBkgColor"), theme.Hex("critBorderColor")),
                { Critical: true } => (theme.Hex("critBkgColor"), theme.Hex("critBorderColor")),
                { Done: true } => (theme.Hex("doneTaskBkgColor"), theme.Hex("doneTaskBorderColor")),
                { Active: true } => (theme.Hex("activeTaskBkgColor"), theme.Hex("activeTaskBorderColor")),
                _ => (theme.Hex("taskBkgColor"), theme.Hex("taskBorderColor"))
            };
            double textLeft, textRight;
            if (task.Milestone)
            {
                var cx = (x0 + x1) / 2;
                var cy = y + barHeight / 2;
                var r = barHeight / 2;
                canvas.Shape(PathGeometry.Polygon([(cx, cy - r), (cx + r, cy), (cx, cy + r), (cx - r, cy)]), fill, stroke, 2);
                textLeft = cx - r;
                textRight = cx + r;
            }
            else
            {
                canvas.Rect(x0, y, Math.Max(1, x1 - x0), barHeight, fill, stroke, 2, 3);
                textLeft = x0;
                textRight = x1;
            }

            var width = context.TextWidth(task.Name, fontSize);
            var baseline = y + barHeight / 2 + fontSize * 0.35;
            if (!task.Milestone && width + 10 <= x1 - x0)
            {
                var inside = task.Done || task.Critical || task.Active ? theme.Hex("taskTextDarkColor") : theme.Hex("taskTextColor");
                canvas.Text(task.Name, (x0 + x1) / 2, baseline, fontSize, TextAnchor.Middle, inside);
            }
            else if (textRight + 5 + width <= left + chartWidth + right)
            {
                canvas.Text(task.Name, textRight + 5, baseline, fontSize, TextAnchor.Start, theme.Hex("taskTextOutsideColor", theme.Hex("textColor")));
            }
            else
            {
                canvas.Text(task.Name, textLeft - 5, baseline, fontSize, TextAnchor.End, theme.Hex("taskTextOutsideColor", theme.Hex("textColor")));
            }
        }

        canvas.Title(info.Title, theme.Hex("titleColor", theme.Hex("textColor")));
        return canvas.Finish(DiagramKind.Gantt, info.Title, info.AccDescription, 10, null);
    }

    /// <summary>One row per task, or in compact mode the first free row of the task's section.</summary>
    private static List<int> AssignRows(GanttModel model)
    {
        var rows = new List<int>(model.Tasks.Count);
        var next = 0;
        var sectionStart = 0;
        var section = -1;
        var lanes = new List<DateTime>();
        foreach (var task in model.Tasks)
        {
            if (task.Section != section)
            {
                section = task.Section;
                sectionStart = next;
                lanes.Clear();
            }

            if (task.Vertical)
            {
                rows.Add(sectionStart);
                continue;
            }

            if (!model.Compact)
            {
                rows.Add(next++);
                continue;
            }

            var lane = lanes.FindIndex(end => end <= task.Start);
            if (lane < 0)
            {
                lanes.Add(task.End);
                lane = lanes.Count - 1;
                next = sectionStart + lanes.Count;
            }
            else
            {
                lanes[lane] = task.End;
            }

            rows.Add(sectionStart + lane);
        }

        return rows;
    }

    private static IEnumerable<DateTime> Ticks(GanttModel model, DateTime min, DateTime max, double chartWidth)
    {
        var (span, months) = (TimeSpan.Zero, 0);
        if (model.TickInterval is { } interval)
        {
            var match = Regex.Match(interval, @"^(\d+)(\w+)$", RegexOptions.CultureInvariant);
            var count = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            (span, months) = match.Groups[2].Value switch
            {
                "millisecond" => (TimeSpan.FromMilliseconds(count), 0),
                "second" => (TimeSpan.FromSeconds(count), 0),
                "minute" => (TimeSpan.FromMinutes(count), 0),
                "hour" => (TimeSpan.FromHours(count), 0),
                "day" => (TimeSpan.FromDays(count), 0),
                "week" => (TimeSpan.FromDays(7 * count), 0),
                _ => (TimeSpan.Zero, count)
            };
        }
        else
        {
            var target = Math.Max(2, Math.Min(10, chartWidth / 70));
            var total = max - min;
            foreach (var step in TickSteps)
            {
                var approx = step.Months > 0 ? TimeSpan.FromDays(30.44 * step.Months) : step.Span;
                (span, months) = step;
                if (total / approx <= target)
                {
                    break;
                }
            }
        }

        DateTime start;
        if (months > 0)
        {
            start = new DateTime(min.Year, min.Month, 1);
            if (months >= 12)
            {
                start = new DateTime(min.Year - min.Year % (months / 12), 1, 1);
            }
        }
        else if (span >= TimeSpan.FromDays(7) && span.TotalDays % 7 == 0)
        {
            start = min.Date.AddDays(-(((int)min.DayOfWeek - (int)model.WeekStart + 7) % 7));
        }
        else if (span >= TimeSpan.FromDays(1))
        {
            start = min.Date;
        }
        else
        {
            var ticks = span.Ticks;
            start = new DateTime(min.Ticks - min.Ticks % ticks);
        }

        var produced = 0;
        for (var t = start; t <= max && produced < 200; produced++)
        {
            if (t >= min)
            {
                yield return t;
            }

            t = months > 0 ? t.AddMonths(months) : t + span;
        }
    }
}
