using System.Text.RegularExpressions;
using MkMermaid.Config;

namespace MkMermaid.Parsing;

internal readonly record struct SourceLine(int Number, string Text)
{
    public string Trimmed => Text.Trim();

    public int Indent
    {
        get
        {
            var indent = 0;
            foreach (var c in Text)
            {
                if (c == ' ')
                {
                    indent++;
                }
                else if (c == '\t')
                {
                    indent += 4;
                }
                else
                {
                    break;
                }
            }

            return indent;
        }
    }
}

/// <summary>Diagram source after front matter, directives, comments and accessibility statements are removed.</summary>
internal sealed partial class Source
{
    private static readonly (string Keyword, DiagramKind Kind)[] Keywords =
    [
        ("flowchart-elk", DiagramKind.Flowchart),
        ("flowchart", DiagramKind.Flowchart),
        ("graph", DiagramKind.Flowchart),
        ("sequenceDiagram", DiagramKind.Sequence),
        ("classDiagram-v2", DiagramKind.Class),
        ("classDiagram", DiagramKind.Class),
        ("stateDiagram-v2", DiagramKind.State),
        ("stateDiagram", DiagramKind.State),
        ("erDiagram", DiagramKind.EntityRelationship),
        ("gantt", DiagramKind.Gantt),
        ("pie", DiagramKind.Pie),
        ("journey", DiagramKind.Journey),
        ("gitGraph", DiagramKind.GitGraph),
        ("quadrantChart", DiagramKind.Quadrant),
        ("requirementDiagram", DiagramKind.Requirement),
        ("requirement", DiagramKind.Requirement),
        ("C4Context", DiagramKind.C4),
        ("C4Container", DiagramKind.C4),
        ("C4Component", DiagramKind.C4),
        ("C4Dynamic", DiagramKind.C4),
        ("C4Deployment", DiagramKind.C4),
        ("mindmap", DiagramKind.Mindmap),
        ("timeline", DiagramKind.Timeline),
        ("sankey-beta", DiagramKind.Sankey),
        ("sankey", DiagramKind.Sankey),
        ("xychart-beta", DiagramKind.XyChart),
        ("xychart", DiagramKind.XyChart),
        ("block-beta", DiagramKind.Block),
        ("block", DiagramKind.Block),
        ("packet-beta", DiagramKind.Packet),
        ("packet", DiagramKind.Packet),
        ("kanban", DiagramKind.Kanban),
        ("architecture-beta", DiagramKind.Architecture),
        ("architecture", DiagramKind.Architecture),
        ("radar-beta", DiagramKind.Radar),
        ("radar", DiagramKind.Radar),
        ("treemap-beta", DiagramKind.Treemap),
        ("treemap", DiagramKind.Treemap),
        ("zenuml", DiagramKind.ZenUml)
    ];

    public required DiagramKind Kind { get; init; }

    /// <summary>Header keyword as written (for example <c>C4Container</c> or <c>graph</c>).</summary>
    public required string Keyword { get; init; }

    /// <summary>Text after the keyword on the header line.</summary>
    public required string HeaderRest { get; init; }

    public required int HeaderLine { get; init; }

    /// <summary>Non-empty lines after the header, with indentation preserved.</summary>
    public required List<SourceLine> Lines { get; init; }

    public required MermaidConfig Config { get; init; }

    public string? Title { get; init; }

    public string? AccTitle { get; init; }

    public string? AccDescription { get; init; }

    [GeneratedRegex(@"%%\{(.*?)\}%%", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Directive();

    [GeneratedRegex(@"^\s*accTitle\s*:\s*(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex AccTitleLine();

    [GeneratedRegex(@"^\s*accDescr\s*:\s*(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex AccDescrLine();

    [GeneratedRegex(@"^\s*accDescr\s*\{\s*(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex AccDescrBlock();

    public static DiagramKind Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return DiagramKind.Unknown;
        }

        try
        {
            return Preprocess(text).Kind;
        }
        catch (MermaidParseException)
        {
            return DiagramKind.Unknown;
        }
    }

    public static Source Preprocess(string text)
    {
        if (text.Length > MermaidEngine.MaxSourceLength)
        {
            throw new MermaidParseException(MermaidErrorCode.TooLong, $"The diagram is longer than {MermaidEngine.MaxSourceLength} characters.", 0);
        }

        var config = new MermaidConfig();
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (normalized.Length > 0 && normalized[0] == '\uFEFF')
        {
            normalized = normalized[1..];
        }

        var raw = normalized.Split('\n').ToList();
        string? title = null;

        // Front matter must be the first line.
        var firstContent = raw.FindIndex(l => l.Trim().Length > 0);
        if (firstContent >= 0 && raw[firstContent].Trim() == "---")
        {
            var end = raw.FindIndex(firstContent + 1, l => l.Trim() == "---");
            if (end < 0)
            {
                throw new MermaidParseException(MermaidErrorCode.UnclosedBlock, "The front matter is not closed with '---'.", firstContent + 1);
            }

            var yaml = YamlReader.Parse(raw.GetRange(firstContent + 1, end - firstContent - 1));
            if (yaml.TryGetValue("title", out var t) && t is not null)
            {
                title = Convert.ToString(t, System.Globalization.CultureInfo.InvariantCulture);
            }

            if (yaml.TryGetValue("config", out var c) && c is Dictionary<string, object?> map)
            {
                config.Merge(map);
            }

            if (yaml.TryGetValue("displayMode", out var displayMode) && displayMode is string mode)
            {
                config.Merge(new Dictionary<string, object?> { ["gantt"] = new Dictionary<string, object?> { ["displayMode"] = mode } });
            }

            for (var i = firstContent; i <= end; i++)
            {
                raw[i] = "";
            }
        }

        // Directives can span lines; blank them out but keep line numbering.
        var joined = string.Join("\n", raw);
        joined = Directive().Replace(joined, m =>
        {
            ApplyDirective(m.Groups[1].Value, config, joined[..m.Index].Count(ch => ch == '\n') + 1);
            return new string('\n', m.Value.Count(ch => ch == '\n'));
        });

        var lines = new List<SourceLine>();
        string? accTitle = null;
        string? accDescription = null;
        var split = joined.Split('\n');
        for (var i = 0; i < split.Length; i++)
        {
            var line = split[i];
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("%%", StringComparison.Ordinal))
            {
                continue;
            }

            if (AccTitleLine().Match(line) is { Success: true } at)
            {
                accTitle = at.Groups[1].Value.Trim();
                continue;
            }

            if (AccDescrLine().Match(line) is { Success: true } ad)
            {
                accDescription = ad.Groups[1].Value.Trim();
                continue;
            }

            if (AccDescrBlock().Match(line) is { Success: true } block)
            {
                var parts = new List<string>();
                var rest = block.Groups[1].Value;
                var closed = false;
                while (true)
                {
                    var close = rest.IndexOf('}');
                    if (close >= 0)
                    {
                        parts.Add(rest[..close].Trim());
                        closed = true;
                        break;
                    }

                    parts.Add(rest.Trim());
                    if (++i >= split.Length)
                    {
                        break;
                    }

                    rest = split[i];
                }

                if (!closed)
                {
                    throw new MermaidParseException(MermaidErrorCode.UnclosedBlock, "accDescr block is not closed with '}'.", i);
                }

                accDescription = string.Join("\n", parts.Where(p => p.Length > 0));
                continue;
            }

            lines.Add(new SourceLine(i + 1, line.TrimEnd()));
        }

        if (lines.Count == 0)
        {
            throw new MermaidParseException(MermaidErrorCode.Empty, "The diagram is empty.", 0);
        }

        var header = lines[0];
        var head = header.Trimmed;
        foreach (var (keyword, kind) in Keywords)
        {
            if (!head.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var after = head[keyword.Length..];
            if (after.Length > 0 && !char.IsWhiteSpace(after[0]) && after[0] is not (':' or ';' or '{'))
            {
                continue;
            }

            lines.RemoveAt(0);
            var rest = after.Trim().TrimStart(':').Trim();
            if (rest.StartsWith(';'))
            {
                rest = rest[1..].Trim();
            }

            return new Source
            {
                Kind = kind,
                Keyword = keyword,
                HeaderRest = rest,
                HeaderLine = header.Number,
                Lines = lines,
                Config = config,
                Title = title,
                AccTitle = accTitle,
                AccDescription = accDescription
            };
        }

        var word = head.Split([' ', '\t', ':', ';'], 2)[0];
        throw new MermaidParseException(MermaidErrorCode.UnknownDiagram, $"Unknown diagram type '{Truncate(word)}'.", header.Number);
    }

    private static void ApplyDirective(string body, MermaidConfig config, int line)
    {
        var text = body.Trim();
        if (text.Equals("wrap", StringComparison.OrdinalIgnoreCase))
        {
            config.Merge(new Dictionary<string, object?> { ["wrap"] = true });
            return;
        }

        object? parsed;
        try
        {
            parsed = Json5Reader.Parse("{" + text + "}");
        }
        catch (FormatException ex)
        {
            throw new MermaidParseException(MermaidErrorCode.InvalidValue, "Invalid directive: " + ex.Message, line);
        }

        if (parsed is not Dictionary<string, object?> map)
        {
            return;
        }

        foreach (var (key, value) in map)
        {
            if (key is "init" or "initialize" or "config" && value is Dictionary<string, object?> settings)
            {
                config.Merge(settings);
            }
            else if (key == "wrap")
            {
                config.Merge(new Dictionary<string, object?> { ["wrap"] = true });
            }
        }
    }

    public static string Truncate(string text, int max = 40) => text.Length <= max ? text : text[..max] + "…";
}
