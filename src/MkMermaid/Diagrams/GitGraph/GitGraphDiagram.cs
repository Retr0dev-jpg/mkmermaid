using System.Globalization;
using System.Text.RegularExpressions;
using MkMermaid.Layout;
using MkMermaid.Parsing;
using MkMermaid.Rendering;
using MkMermaid.Scene;
using static MkMermaid.Parsing.ParseHelpers;

namespace MkMermaid.Diagrams.GitGraph;

internal enum CommitType
{
    Normal,
    Reverse,
    Highlight,
    Merge,
    CherryPick
}

internal sealed class Commit
{
    public required string Id { get; init; }

    public required int Sequence { get; init; }

    public required string Branch { get; init; }

    public required CommitType Type { get; init; }

    public required List<string> Parents { get; init; }

    public List<string> Tags { get; init; } = [];

    /// <summary>Label shown under the commit; auto-generated ids are not shown when commit labels are off.</summary>
    public bool CustomId { get; init; }
}

internal sealed record Branch(string Name, int Index, double Order);

internal sealed class GitGraphModel : IDiagramModel
{
    public string? Title => null;

    public LayoutDirection Direction { get; set; } = LayoutDirection.LeftToRight;

    public List<Branch> Branches { get; } = [];

    public List<Commit> Commits { get; } = [];

    public DiagramScene Render(RenderContext context, DiagramInfo info) => GitGraphRenderer.Render(this, context, info);
}

internal static partial class GitGraphParser
{
    [GeneratedRegex(@"(?<key>id|tag|type|order|parent)\s*:\s*(?:""(?<value>[^""]*)""|(?<value>[^\s""]+))", RegexOptions.CultureInvariant)]
    private static partial Regex Attribute();

    public static GitGraphModel Parse(Source source)
    {
        var model = new GitGraphModel();
        var header = source.HeaderRest.TrimEnd(':').Trim();
        model.Direction = header switch
        {
            "TB" => LayoutDirection.TopToBottom,
            "BT" => LayoutDirection.BottomToTop,
            _ => LayoutDirection.LeftToRight
        };

        var mainName = source.Config.Text("gitGraph", "mainBranchName") ?? "main";
        var mainOrder = source.Config.Number("gitGraph", "mainBranchOrder", 0);
        model.Branches.Add(new Branch(mainName, 0, mainOrder));
        var heads = new Dictionary<string, string?>(StringComparer.Ordinal) { [mainName] = null };
        var commits = new Dictionary<string, Commit>(StringComparer.Ordinal);
        var current = mainName;

        Commit Add(CommitType type, string? id, List<string> parents, List<string> tags, int line)
        {
            CheckCount(model.Commits.Count + 1, MermaidEngine.MaxElements, line, "commits");
            var sequence = model.Commits.Count;
            var commit = new Commit
            {
                Id = id ?? AutoId(sequence),
                Sequence = sequence,
                Branch = current,
                Type = type,
                Parents = parents,
                Tags = tags,
                CustomId = id is not null
            };
            if (!commits.TryAdd(commit.Id, commit))
            {
                throw Error(line, $"A commit with id '{Source.Truncate(commit.Id)}' already exists.");
            }

            model.Commits.Add(commit);
            heads[current] = commit.Id;
            return commit;
        }

        foreach (var line in source.Lines)
        {
            var text = line.Trimmed;
            var word = text.Split([' ', '\t'], 2)[0];
            var rest = text.Length > word.Length ? text[word.Length..].Trim() : "";
            var attributes = Attributes(rest);
            switch (word)
            {
                case "commit":
                    var type = attributes.TryGetValue("type", out var t) ? t.ToUpperInvariant() switch
                    {
                        "REVERSE" => CommitType.Reverse,
                        "HIGHLIGHT" => CommitType.Highlight,
                        "NORMAL" => CommitType.Normal,
                        _ => throw new MermaidParseException(MermaidErrorCode.InvalidValue, $"Unknown commit type '{Source.Truncate(t)}'.", line.Number)
                    } : CommitType.Normal;
                    Add(type, attributes.GetValueOrDefault("id"), heads[current] is { } head ? [head] : [], Tags(rest), line.Number);
                    break;
                case "branch":
                    var name = BranchName(rest, line);
                    if (heads.ContainsKey(name))
                    {
                        throw Error(line, $"Branch '{Source.Truncate(name)}' already exists.");
                    }

                    CheckCount(model.Branches.Count + 1, 256, line.Number, "branches");
                    var order = attributes.TryGetValue("order", out var o) && double.TryParse(o, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : model.Branches.Count;
                    model.Branches.Add(new Branch(name, model.Branches.Count, order));
                    heads[name] = heads[current];
                    current = name;
                    break;
                case "checkout" or "switch":
                    var target = BranchName(rest, line);
                    if (!heads.ContainsKey(target))
                    {
                        throw new MermaidParseException(MermaidErrorCode.UnknownReference, $"Cannot checkout '{Source.Truncate(target)}': the branch does not exist.", line.Number);
                    }

                    current = target;
                    break;
                case "merge":
                    var other = BranchName(rest, line);
                    if (!heads.TryGetValue(other, out var otherHead))
                    {
                        throw new MermaidParseException(MermaidErrorCode.UnknownReference, $"Cannot merge '{Source.Truncate(other)}': the branch does not exist.", line.Number);
                    }

                    if (other == current)
                    {
                        throw Error(line, "A branch cannot be merged into itself.");
                    }

                    if (otherHead is null || heads[current] is null)
                    {
                        throw Error(line, $"Cannot merge '{Source.Truncate(other)}': both branches need at least one commit.");
                    }

                    if (otherHead == heads[current] || IsAncestor(commits, otherHead, heads[current]!))
                    {
                        throw Error(line, $"Cannot merge '{Source.Truncate(other)}': it has no commits that are not already in '{Source.Truncate(current)}'.");
                    }

                    var mergeType = attributes.TryGetValue("type", out var mt) && mt.Equals("REVERSE", StringComparison.OrdinalIgnoreCase) ? CommitType.Reverse
                        : attributes.TryGetValue("type", out mt) && mt.Equals("HIGHLIGHT", StringComparison.OrdinalIgnoreCase) ? CommitType.Highlight
                        : CommitType.Merge;
                    Add(mergeType, attributes.GetValueOrDefault("id"), [heads[current]!, otherHead], Tags(rest), line.Number);
                    break;
                case "cherry-pick":
                    if (!attributes.TryGetValue("id", out var picked) || !commits.TryGetValue(picked, out var pickedCommit))
                    {
                        throw new MermaidParseException(MermaidErrorCode.UnknownReference, "cherry-pick needs the id of an existing commit.", line.Number);
                    }

                    if (pickedCommit.Branch == current)
                    {
                        throw Error(line, "A commit cannot be cherry-picked onto its own branch.");
                    }

                    if (heads[current] is not { } currentHead)
                    {
                        throw Error(line, "Cherry-pick needs at least one commit on the current branch.");
                    }

                    var tags = Tags(rest);
                    if (!rest.Contains("tag:", StringComparison.Ordinal))
                    {
                        tags = ["cherry-pick:" + picked];
                    }

                    Add(CommitType.CherryPick, null, [currentHead, picked], tags, line.Number);
                    break;
                default:
                    throw Error(line, $"Unrecognized statement '{Source.Truncate(text)}'.");
            }
        }

        return model;
    }

    private static string AutoId(int sequence)
    {
        var hash = 2166136261u;
        foreach (var c in sequence.ToString(CultureInfo.InvariantCulture))
        {
            hash = (hash ^ c) * 16777619u;
        }

        return sequence.ToString(CultureInfo.InvariantCulture) + "-" + hash.ToString("x8", CultureInfo.InvariantCulture)[..7];
    }

    private static bool IsAncestor(Dictionary<string, Commit> commits, string candidate, string of)
    {
        var stack = new Stack<string>([of]);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (stack.TryPop(out var id))
        {
            if (id == candidate)
            {
                return true;
            }

            if (seen.Add(id) && commits.TryGetValue(id, out var commit))
            {
                foreach (var parent in commit.Parents)
                {
                    stack.Push(parent);
                }
            }
        }

        return false;
    }

    private static string BranchName(string rest, SourceLine line)
    {
        var name = rest.Split([' ', '\t'], 2)[0].Trim();
        name = Unquote(name);
        if (name.Length == 0)
        {
            throw Error(line, "A branch name is required.");
        }

        return name;
    }

    private static Dictionary<string, string> Attributes(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Attribute().Matches(text))
        {
            if (m.Groups["key"].Value != "tag")
            {
                map[m.Groups["key"].Value] = m.Groups["value"].Value;
            }
        }

        return map;
    }

    private static List<string> Tags(string text) =>
        Attribute().Matches(text).Where(m => m.Groups["key"].Value == "tag").Select(m => m.Groups["value"].Value).ToList();
}

internal static class GitGraphRenderer
{
    private const double CommitStep = 50;
    private const double LaneStep = 60;
    private const double Radius = 10;

    public static DiagramScene Render(GitGraphModel model, RenderContext context, DiagramInfo info)
    {
        var theme = context.Theme;
        var config = context.Config;
        var showBranches = config.Flag("gitGraph", "showBranches", true);
        var showLabels = config.Flag("gitGraph", "showCommitLabel", true);
        var rotate = config.Flag("gitGraph", "rotateCommitLabel", true);
        var parallel = config.Flag("gitGraph", "parallelCommits", false);
        var vertical = model.Direction != LayoutDirection.LeftToRight;
        var lanes = model.Branches.OrderBy(b => b.Order).ThenBy(b => b.Index).Select((b, i) => (b.Name, i)).ToDictionary(p => p.Name, p => p.i, StringComparer.Ordinal);
        var colorOf = model.Branches.ToDictionary(b => b.Name, b => b.Index % 8, StringComparer.Ordinal);
        var labels = model.Branches.ToDictionary(b => b.Name, b => context.MeasurePlain(b.Name, 14), StringComparer.Ordinal);
        var labelSpace = showBranches ? labels.Values.Max(l => l.Width) + 30 : 10;
        var canvas = context.CreateCanvas();
        var commits = model.Commits.ToDictionary(c => c.Id, StringComparer.Ordinal);

        // Position along the time axis: sequential, or as early as the parents allow when parallelCommits is on.
        var step = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var commit in model.Commits)
        {
            step[commit.Id] = parallel ? (commit.Parents.Count == 0 ? 0 : commit.Parents.Max(p => step[p]) + 1) : commit.Sequence;
        }

        var maxStep = step.Count == 0 ? 0 : step.Values.Max();
        Point Position(Commit c)
        {
            var along = labelSpace + Radius + 20 + step[c.Id] * CommitStep;
            if (model.Direction == LayoutDirection.BottomToTop)
            {
                along = labelSpace + Radius + 20 + (maxStep - step[c.Id]) * CommitStep;
            }

            var across = lanes[c.Branch] * LaneStep;
            return vertical ? new Point(across, along) : new Point(along, across);
        }

        var end = labelSpace + Radius + 40 + maxStep * CommitStep;
        foreach (var branch in model.Branches)
        {
            var across = lanes[branch.Name] * LaneStep;
            if (vertical)
            {
                canvas.Line(across, labelSpace, across, end, theme.Hex("lineColor"), 1, [2, 2]);
            }
            else
            {
                canvas.Line(labelSpace, across, end, across, theme.Hex("lineColor"), 1, [2, 2]);
            }
        }

        // Arrows from parents. Only a merged-in branch bends next to the merge commit; an arrow whose straight
        // run would cross other commits detours on a free line between the two lanes.
        var detours = lanes.Values.Select(l => l * LaneStep).ToList();
        foreach (var commit in model.Commits)
        {
            var to = Position(commit);
            foreach (var parentId in commit.Parents)
            {
                var parent = commits[parentId];
                var mergedIn = commit.Type == CommitType.Merge && parentId != commit.Parents[0];
                var color = theme.Hex("git" + colorOf[mergedIn ? parent.Branch : commit.Branch]);
                var runLane = mergedIn ? parent.Branch : commit.Branch;
                var (low, high) = (step[parentId], step[commit.Id]);
                double? detour = null;
                if (parent.Branch != commit.Branch && model.Commits.Any(c => c.Branch == runLane && step[c.Id] > low && step[c.Id] < high))
                {
                    detour = FreeLine((lanes[parent.Branch] + lanes[commit.Branch]) * LaneStep / 2, detours);
                }

                canvas.Shape(Connector(Position(parent), to, vertical, bendAtStart: !mergedIn, detour), null, color, 8);
            }
        }

        // Commits, labels and tags.
        foreach (var commit in model.Commits)
        {
            var p = Position(commit);
            var color = theme.Hex("git" + colorOf[commit.Branch]);
            var inverse = theme.Hex("gitInv" + colorOf[commit.Branch]);
            switch (commit.Type)
            {
                case CommitType.Highlight:
                    canvas.Rect(p.X - Radius, p.Y - Radius, 2 * Radius, 2 * Radius, inverse, color, 3);
                    break;
                case CommitType.Reverse:
                    canvas.Circle(p.X, p.Y, Radius, color, color);
                    canvas.Line(p.X - 5, p.Y - 5, p.X + 5, p.Y + 5, theme.Hex("background", "#FFFFFF"), 3);
                    canvas.Line(p.X - 5, p.Y + 5, p.X + 5, p.Y - 5, theme.Hex("background", "#FFFFFF"), 3);
                    break;
                case CommitType.Merge:
                    canvas.Circle(p.X, p.Y, Radius, color, color);
                    canvas.Circle(p.X, p.Y, 6, theme.Hex("background", "#FFFFFF"), null);
                    break;
                case CommitType.CherryPick:
                    canvas.Circle(p.X, p.Y, Radius, color, color);
                    canvas.Circle(p.X - 3, p.Y + 2, 2.75, "#FFFFFF", null);
                    canvas.Circle(p.X + 3, p.Y + 2, 2.75, "#FFFFFF", null);
                    canvas.Line(p.X + 3, p.Y + 1, p.X, p.Y - 5, "#FFFFFF", 1);
                    canvas.Line(p.X - 3, p.Y + 1, p.X, p.Y - 5, "#FFFFFF", 1);
                    break;
                default:
                    canvas.Circle(p.X, p.Y, Radius, color, color);
                    break;
            }

            if (showLabels && (commit.Type is not (CommitType.Merge or CommitType.CherryPick) || commit.CustomId))
            {
                var width = context.TextWidth(commit.Id, 10);
                if (vertical)
                {
                    canvas.Rect(p.X - width - 22, p.Y - 7, width + 4, 14, theme.Hex("commitLabelBackground"), null, 0, opacity: 0.5);
                    canvas.Text(commit.Id, p.X - 20, p.Y + 3.5, 10, TextAnchor.End, theme.Hex("commitLabelColor"));
                }
                else if (rotate)
                {
                    canvas.Text(commit.Id, p.X - 2, p.Y + Radius + 10, 10, TextAnchor.End, theme.Hex("commitLabelColor"), rotation: -45);
                }
                else
                {
                    canvas.Rect(p.X - width / 2 - 2, p.Y + Radius + 6, width + 4, 14, theme.Hex("commitLabelBackground"), null, 0, opacity: 0.5);
                    canvas.Text(commit.Id, p.X, p.Y + Radius + 17, 10, TextAnchor.Middle, theme.Hex("commitLabelColor"));
                }
            }

            var tagOffset = 0.0;
            foreach (var tag in commit.Tags)
            {
                var width = context.TextWidth(tag, 10) + 8;
                var y = p.Y - Radius - 22 - tagOffset;
                var x = p.X - width / 2;
                if (vertical)
                {
                    x = p.X + Radius + 14 + tagOffset;
                    y = p.Y - 8;
                    canvas.Shape(PathGeometry.Polygon([(x - 6, y + 8), (x, y), (x + width, y), (x + width, y + 16), (x, y + 16)]),
                        theme.Hex("tagLabelBackground"), theme.Hex("tagLabelBorder"));
                    canvas.Text(tag, x + 4, y + 12, 10, TextAnchor.Start, theme.Hex("tagLabelColor"));
                    tagOffset += width + 12;
                    continue;
                }

                canvas.Shape(PathGeometry.Polygon([(x, y), (x + width, y), (x + width, y + 16), (p.X + 4, y + 16), (p.X, y + 20), (p.X - 4, y + 16), (x, y + 16)]),
                    theme.Hex("tagLabelBackground"), theme.Hex("tagLabelBorder"));
                canvas.Circle(x + 4, y + 8, 1.5, theme.Hex("tagLabelColor"), null);
                canvas.Text(tag, p.X + 2, y + 12, 10, TextAnchor.Middle, theme.Hex("tagLabelColor"));
                tagOffset += 22;
            }
        }

        if (showBranches)
        {
            foreach (var branch in model.Branches)
            {
                var label = labels[branch.Name];
                var across = lanes[branch.Name] * LaneStep;
                var index = colorOf[branch.Name];
                var w = label.Width + 16;
                var h = label.Height + 4;
                var (x, y) = vertical ? (across - w / 2, labelSpace - h - 6) : (labelSpace - w - 10, across - h / 2);
                canvas.Rect(x, y, w, h, theme.Hex("git" + index), null, 0, 4);
                canvas.LabelCentered(label, x + w / 2, y + h / 2, theme.Hex("gitBranchLabel" + index));
            }
        }

        canvas.Title(info.Title, theme.Hex("textColor"));
        return canvas.Finish(DiagramKind.GitGraph, info.Title, info.AccDescription, 10, null);
    }

    /// <summary>Picks a detour line near <paramref name="preferred"/> that keeps clear of lanes and detours already drawn.</summary>
    private static double FreeLine(double preferred, List<double> used)
    {
        var line = preferred;
        for (var attempt = 1; used.Any(u => Math.Abs(u - line) < 20) && attempt < 8; attempt++)
        {
            line = preferred + (attempt % 2 == 1 ? 1 : -1) * ((attempt + 1) / 2) * 10;
        }

        used.Add(line);
        return line;
    }

    /// <summary>
    /// Straight line on one lane. Across lanes the line bends next to the parent when a branch starts
    /// (<paramref name="bendAtStart"/>) and next to the commit when another branch is merged in, or runs
    /// along <paramref name="detour"/> when the direct path would cross other commits.
    /// </summary>
    private static PathGeometry Connector(Point from, Point to, bool vertical, bool bendAtStart, double? detour = null)
    {
        // Work in (along, across) coordinates so both orientations share one path.
        Point Map(double along, double across) => vertical ? new Point(across, along) : new Point(along, across);
        var (fa, fc) = vertical ? (from.Y, from.X) : (from.X, from.Y);
        var (ta, tc) = vertical ? (to.Y, to.X) : (to.X, to.Y);
        var path = new PathGeometry().MoveTo(from.X, from.Y);
        if (Math.Abs(fc - tc) < 0.5)
        {
            return path.LineTo(to.X, to.Y);
        }

        const double r = 20;
        var forward = Math.Sign(ta - fa) is 0 ? 1 : Math.Sign(ta - fa);
        var side = Math.Sign(tc - fc);
        if (detour is { } line)
        {
            var radius = Math.Min(r, Math.Min(Math.Abs(line - fc), Math.Abs(tc - line)) - 1);
            Point[] corners =
            [
                Map(fa, line - side * radius), Map(fa, line - side * radius / 2), Map(fa + forward * radius / 2, line), Map(fa + forward * radius, line),
                Map(ta - forward * radius, line), Map(ta - forward * radius / 2, line), Map(ta, line + side * radius / 2), Map(ta, line + side * radius),
            ];
            path.LineTo(corners[0].X, corners[0].Y).CubicTo(corners[1].X, corners[1].Y, corners[2].X, corners[2].Y, corners[3].X, corners[3].Y);
            path.LineTo(corners[4].X, corners[4].Y).CubicTo(corners[5].X, corners[5].Y, corners[6].X, corners[6].Y, corners[7].X, corners[7].Y);
            return path.LineTo(to.X, to.Y);
        }

        Point a, c1, c2, b;
        if (bendAtStart)
        {
            a = Map(fa, tc - side * r);
            c1 = Map(fa, tc - side * r / 2);
            c2 = Map(fa + forward * r / 2, tc);
            b = Map(fa + forward * r, tc);
        }
        else
        {
            a = Map(ta - forward * r, fc);
            c1 = Map(ta - forward * r / 2, fc);
            c2 = Map(ta, fc + side * r / 2);
            b = Map(ta, fc + side * r);
        }

        return path.LineTo(a.X, a.Y).CubicTo(c1.X, c1.Y, c2.X, c2.Y, b.X, b.Y).LineTo(to.X, to.Y);
    }
}
