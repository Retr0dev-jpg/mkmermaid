using System.Text.RegularExpressions;
using MkMermaid.Parsing;
using MkMermaid.Rendering;
using MkMermaid.Theming;
using static MkMermaid.Parsing.ParseHelpers;

namespace MkMermaid.Diagrams.Sequence;

internal static partial class SequenceParser
{
    private const int MaxDepth = 32;

    [GeneratedRegex(@"^(?:(?<create>create)\s+)?(?<kind>participant|actor)\s+(?<rest>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ParticipantLine();

    [GeneratedRegex(@"^note\s+(?<place>left\s+of|right\s+of|over)\s+(?<actors>[^:]+?)\s*:\s*(?<text>.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NoteLine();

    [GeneratedRegex(@"^(?<from>[^:]+?)\s*(?<arrow><<-->>|<<->>|-->>|->>|-->|->|--x|-x|--\)|-\))\s*(?<act>[+-]?)\s*(?<to>[^\s:+\-<>][^:]*?)\s*(?::(?<text>.*))?$", RegexOptions.CultureInvariant)]
    private static partial Regex MessageLine();

    public static SequenceModel Parse(Source source)
    {
        var model = new SequenceModel();
        var open = new Stack<(bool Box, BlockKind Kind, int Line)>();
        var currentBox = -1;
        var messages = 0;
        foreach (var line in source.Lines)
        {
            var text = line.Trimmed;
            if (text.EndsWith(';') && !ParseHelpers.ClosesEntity(text, text.Length - 1))
            {
                text = text[..^1].TrimEnd();
            }

            if (text.Length == 0)
            {
                continue;
            }

            var word = text.Split([' ', '\t', ':'], 2)[0].ToLowerInvariant();
            var rest = text.Length > word.Length ? text[word.Length..].Trim() : "";
            switch (word)
            {
                case "title":
                    model.Title = rest.TrimStart(':').Trim();
                    continue;
                case "autonumber":
                    model.Events.Add(Autonumber(line, rest));
                    continue;
                case "activate" or "deactivate" when rest.Length > 0:
                    model.Ensure(rest);
                    model.Events.Add(new ActivationEvent(line.Number, rest, word == "activate"));
                    continue;
                case "destroy" when rest.Length > 0:
                    model.Ensure(rest).Destroyed = true;
                    model.Events.Add(new DestroyEvent(line.Number, rest));
                    continue;
                case "box":
                    if (currentBox >= 0)
                    {
                        throw Error(line, "Boxes cannot be nested.");
                    }

                    model.Boxes.Add(Box(rest));
                    currentBox = model.Boxes.Count - 1;
                    open.Push((true, BlockKind.Rect, line.Number));
                    continue;
                case "loop" or "alt" or "opt" or "par" or "par_over" or "critical" or "break" or "rect":
                    if (open.Count >= MaxDepth)
                    {
                        throw new MermaidParseException(MermaidErrorCode.TooDeep, $"Blocks are nested more than {MaxDepth} levels deep.", line.Number);
                    }

                    var kind = word switch
                    {
                        "loop" => BlockKind.Loop,
                        "alt" => BlockKind.Alt,
                        "opt" => BlockKind.Opt,
                        "par" or "par_over" => BlockKind.Par,
                        "critical" => BlockKind.Critical,
                        "break" => BlockKind.Break,
                        _ => BlockKind.Rect
                    };
                    open.Push((false, kind, line.Number));
                    model.Events.Add(new BlockStartEvent(line.Number, kind, rest));
                    continue;
                case "else" or "and" or "option":
                    if (!open.TryPeek(out var top) || top.Box || (word, top.Kind) is not (("else", BlockKind.Alt) or ("and", BlockKind.Par) or ("option", BlockKind.Critical)))
                    {
                        throw Error(line, $"'{word}' is only valid inside {(word == "else" ? "alt" : word == "and" ? "par" : "critical")}.");
                    }

                    model.Events.Add(new BlockDividerEvent(line.Number, rest));
                    continue;
                case "end":
                    if (!open.TryPop(out var closed))
                    {
                        throw Error(line, "'end' without an open block.");
                    }

                    if (closed.Box)
                    {
                        currentBox = -1;
                    }
                    else
                    {
                        model.Events.Add(new BlockEndEvent(line.Number));
                    }

                    continue;
                case "links" or "link" or "properties" or "details":
                    continue;
            }

            if (ParticipantLine().Match(text) is { Success: true } p)
            {
                AddParticipant(model, line, p, currentBox);
                continue;
            }

            if (NoteLine().Match(text) is { Success: true } n)
            {
                var actors = n.Groups["actors"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (actors.Length is 0 or > 2)
                {
                    throw Error(line, "A note refers to one or two participants.");
                }

                foreach (var a in actors)
                {
                    model.Ensure(a);
                }

                var place = n.Groups["place"].Value.ToLowerInvariant();
                var placement = place.StartsWith("left", StringComparison.Ordinal) ? NotePlacement.LeftOf : place.StartsWith("right", StringComparison.Ordinal) ? NotePlacement.RightOf : NotePlacement.Over;
                model.Events.Add(new NoteEvent(line.Number, placement, actors[0], actors.Length > 1 ? actors[1] : null, n.Groups["text"].Value.Trim()));
                continue;
            }

            if (MessageLine().Match(text) is { Success: true } m)
            {
                CheckCount(++messages, MermaidEngine.MaxConnections, line.Number, "messages");
                var from = m.Groups["from"].Value.Trim();
                var to = m.Groups["to"].Value.Trim();
                model.Ensure(from);
                model.Ensure(to);
                var arrow = m.Groups["arrow"].Value;
                var head = arrow.TrimStart('<').TrimStart('-') switch
                {
                    "x" => Marker.Cross,
                    ")" => Marker.OpenArrow,
                    ">>" => Marker.Arrow,
                    _ => Marker.None
                };
                var act = m.Groups["act"].Value;
                model.Events.Add(new MessageEvent(
                    line.Number, from, to, arrow.Contains("--", StringComparison.Ordinal), head, arrow.StartsWith("<<", StringComparison.Ordinal),
                    m.Groups["text"].Value.Trim(), act == "+", act == "-"));
                continue;
            }

            throw Error(line, $"Unrecognized statement '{Source.Truncate(text)}'.");
        }

        if (open.TryPeek(out var unclosed))
        {
            throw new MermaidParseException(MermaidErrorCode.UnclosedBlock, "A block is not closed with 'end'.", unclosed.Line);
        }

        CheckCount(model.Participants.Count, MermaidEngine.MaxElements, source.HeaderLine, "participants");
        return model;
    }

    private static void AddParticipant(SequenceModel model, SourceLine line, Match match, int box)
    {
        var rest = match.Groups["rest"].Value.Trim();
        string? metadata = null;
        var at = rest.IndexOf("@{", StringComparison.Ordinal);
        string id;
        string? alias = null;
        if (at > 0)
        {
            var close = rest.LastIndexOf('}');
            if (close < at)
            {
                throw Error(line, "Participant metadata is not closed with '}'.");
            }

            id = rest[..at].Trim();
            metadata = rest[(at + 2)..close];
            var after = rest[(close + 1)..].Trim();
            if (after.StartsWith("as ", StringComparison.OrdinalIgnoreCase))
            {
                alias = after[3..].Trim();
            }
        }
        else
        {
            var asIndex = rest.IndexOf(" as ", StringComparison.OrdinalIgnoreCase);
            id = asIndex > 0 ? rest[..asIndex].Trim() : rest;
            alias = asIndex > 0 ? rest[(asIndex + 4)..].Trim() : null;
        }

        if (id.Length == 0)
        {
            throw Error(line, "A participant needs a name.");
        }

        var participant = model.Ensure(id);
        participant.Kind = match.Groups["kind"].Value.Equals("actor", StringComparison.OrdinalIgnoreCase) ? ParticipantKind.Actor : ParticipantKind.Participant;
        if (alias is { Length: > 0 })
        {
            participant.Label = alias;
        }

        if (metadata is not null)
        {
            var map = InlineMap(metadata.Replace("\"", "", StringComparison.Ordinal));
            if (map.TryGetValue("type", out var type))
            {
                participant.Kind = type.ToLowerInvariant() switch
                {
                    "actor" => ParticipantKind.Actor,
                    "boundary" => ParticipantKind.Boundary,
                    "control" => ParticipantKind.Control,
                    "entity" => ParticipantKind.Entity,
                    "database" => ParticipantKind.Database,
                    "collections" => ParticipantKind.Collections,
                    "queue" => ParticipantKind.Queue,
                    _ => ParticipantKind.Participant
                };
            }

            if (map.TryGetValue("alias", out var label) && label.Length > 0)
            {
                participant.Label = label;
            }
        }

        if (box >= 0)
        {
            participant.Box = box;
        }

        if (match.Groups["create"].Success)
        {
            participant.Created = true;
            model.Events.Add(new CreateEvent(line.Number, id));
        }
    }

    private static AutonumberEvent Autonumber(SourceLine line, string rest)
    {
        if (rest.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            return new AutonumberEvent(line.Number, 1, 1, true);
        }

        var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var start = 1;
        var step = 1;
        if (parts.Length > 0 && !int.TryParse(parts[0], out start) || parts.Length > 1 && !int.TryParse(parts[1], out step))
        {
            throw Error(line, "autonumber takes a start and a step.");
        }

        return new AutonumberEvent(line.Number, start, step, false);
    }

    private static SequenceBox Box(string rest)
    {
        if (rest.Length == 0)
        {
            return new SequenceBox(null, null);
        }

        var functional = Regex.Match(rest, @"^(rgba?|hsla?)\([^)]*\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var token = functional.Success ? functional.Value : rest.Split(' ', 2)[0];
        if (token.Equals("transparent", StringComparison.OrdinalIgnoreCase) || Color.TryParse(token, out _))
        {
            var label = rest[token.Length..].Trim();
            return new SequenceBox(label.Length > 0 ? label : null, token);
        }

        return new SequenceBox(rest, null);
    }
}
