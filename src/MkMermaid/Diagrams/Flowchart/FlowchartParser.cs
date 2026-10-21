using System.Text.RegularExpressions;
using MkMermaid.Parsing;
using MkMermaid.Rendering;

namespace MkMermaid.Diagrams.Flowchart;

internal static partial class FlowchartParser
{
    private static readonly (string Open, string Close, NodeShape Shape, string? AltClose, NodeShape AltShape)[] Openers =
    [
        ("(((", ")))", NodeShape.DoubleCircle, null, default),
        ("((", "))", NodeShape.Circle, null, default),
        ("([", "])", NodeShape.Stadium, null, default),
        ("(", ")", NodeShape.Rounded, null, default),
        ("[[", "]]", NodeShape.Subroutine, null, default),
        ("[(", ")]", NodeShape.Cylinder, null, default),
        ("[/", "/]", NodeShape.LeanRight, "\\]", NodeShape.Trapezoid),
        ("[\\", "\\]", NodeShape.LeanLeft, "/]", NodeShape.TrapezoidAlt),
        ("[", "]", NodeShape.Rect, null, default),
        ("{{", "}}", NodeShape.Hexagon, null, default),
        ("{", "}", NodeShape.Diamond, null, default),
        (">", "]", NodeShape.Asymmetric, null, default)
    ];

    [GeneratedRegex(@"^\s*(?<s>[<xo])?(?<l>-{2,}|={2,}|-\.+-|~{3,})(?<e>[>xo])?", RegexOptions.CultureInvariant)]
    private static partial Regex NormalLink();

    [GeneratedRegex(@"^\s*(?<s>[<xo])?(?<o>--|==|-\.)(?=\s)", RegexOptions.CultureInvariant)]
    private static partial Regex TextLinkOpen();

    [GeneratedRegex(@"^\s*\|(?<t>[^|]*)\|", RegexOptions.CultureInvariant)]
    private static partial Regex PipeLabel();

    [GeneratedRegex(@"^\s*(?<id>[\p{L}\p{N}_][\p{L}\p{N}_-]*)@(?=[-=.~<xo])", RegexOptions.CultureInvariant)]
    private static partial Regex EdgeId();

    public static FlowchartModel Parse(Source source)
    {
        var model = new FlowchartModel();
        if (FlowchartModel.ParseDirection(source.HeaderRest.Split(' ', 2)[0]) is { } direction)
        {
            model.Direction = direction;
        }

        var state = new State(model);
        foreach (var line in source.Lines)
        {
            foreach (var part in ParseHelpers.SplitTopLevel(line.Text, ';'))
            {
                var statement = part.Trim();
                if (statement.Length > 0)
                {
                    Statement(state, statement, line.Number);
                }
            }
        }

        if (state.Stack.Count > 0)
        {
            throw new MermaidParseException(MermaidErrorCode.UnclosedBlock, $"Subgraph '{state.Stack.Peek()}' is not closed with 'end'.", source.Lines[^1].Number);
        }

        // Nodes that only name a subgraph are references to it.
        foreach (var id in model.Subgraphs.Keys)
        {
            if (model.Nodes.TryGetValue(id, out var node) && !node.Explicit)
            {
                model.Nodes.Remove(id);
            }
        }

        foreach (var edge in model.Edges)
        {
            if (!model.Nodes.ContainsKey(edge.From) && !model.Subgraphs.ContainsKey(edge.From) ||
                !model.Nodes.ContainsKey(edge.To) && !model.Subgraphs.ContainsKey(edge.To))
            {
                throw new MermaidParseException(MermaidErrorCode.UnknownReference, "Edge references an unknown node.", 0);
            }
        }

        foreach (var (id, style) in state.LinkStyles)
        {
            if (id >= 0 && id < model.Edges.Count)
            {
                model.Edges[id].Style = model.Edges[id].Style.Merge(style.Style);
                model.Edges[id].Curve = style.Curve ?? model.Edges[id].Curve;
            }
        }

        return model;
    }

    private sealed class State(FlowchartModel model)
    {
        public FlowchartModel Model { get; } = model;

        public Stack<string> Stack { get; } = new();

        public List<(int Index, (Style Style, string? Curve) Style)> LinkStyles { get; } = [];

        public Dictionary<string, (Style Style, string? Curve)> EdgeData { get; } = new(StringComparer.Ordinal);
    }

    private static void Statement(State state, string statement, int line)
    {
        var model = state.Model;
        if (ParseHelpers.StartsWithWord(statement, "direction"))
        {
            var dir = FlowchartModel.ParseDirection(statement[9..]) ?? throw ParseHelpers.Error(line, "Unknown direction.");
            if (state.Stack.Count > 0)
            {
                model.Subgraphs[state.Stack.Peek()].Direction = dir;
            }
            else
            {
                model.Direction = dir;
            }

            return;
        }

        if (ParseHelpers.StartsWithWord(statement, "subgraph"))
        {
            OpenSubgraph(state, statement[8..].Trim(), line);
            return;
        }

        if (statement == "end")
        {
            if (state.Stack.Count == 0)
            {
                throw ParseHelpers.Error(line, "'end' without an open subgraph.");
            }

            state.Stack.Pop();
            return;
        }

        if (ParseHelpers.StartsWithWord(statement, "classDef"))
        {
            var rest = statement[8..].Trim();
            var space = rest.IndexOfAny([' ', '\t']);
            if (space < 0)
            {
                throw ParseHelpers.Error(line, "classDef needs a name and styles.");
            }

            var style = Style.Parse(rest[space..]);
            foreach (var name in rest[..space].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                model.ClassDefs[name] = model.ClassDefs.TryGetValue(name, out var existing) ? existing.Merge(style) : style;
            }

            return;
        }

        if (ParseHelpers.StartsWithWord(statement, "class"))
        {
            var rest = statement[5..].Trim();
            var space = rest.LastIndexOfAny([' ', '\t']);
            if (space < 0)
            {
                throw ParseHelpers.Error(line, "class needs node ids and a class name.");
            }

            var className = rest[(space + 1)..].Trim();
            foreach (var id in rest[..space].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (model.Subgraphs.TryGetValue(id, out var subgraph))
                {
                    subgraph.Classes.Add(className);
                }
                else
                {
                    Node(state, id, line).Classes.Add(className);
                }
            }

            return;
        }

        if (ParseHelpers.StartsWithWord(statement, "style"))
        {
            var rest = statement[5..].Trim();
            var space = rest.IndexOfAny([' ', '\t']);
            if (space < 0)
            {
                throw ParseHelpers.Error(line, "style needs a node id and styles.");
            }

            var id = rest[..space];
            var style = Style.Parse(rest[space..]);
            if (model.Subgraphs.TryGetValue(id, out var subgraph))
            {
                subgraph.Style = subgraph.Style.Merge(style);
            }
            else
            {
                var node = Node(state, id, line);
                node.Style = node.Style.Merge(style);
            }

            return;
        }

        if (ParseHelpers.StartsWithWord(statement, "linkStyle"))
        {
            LinkStyle(state, statement[9..].Trim(), line);
            return;
        }

        if (ParseHelpers.StartsWithWord(statement, "click") || ParseHelpers.StartsWithWord(statement, "call") || ParseHelpers.StartsWithWord(statement, "href"))
        {
            // Interactions are not supported in a static renderer.
            return;
        }

        Chain(state, statement, line);
    }

    private static void OpenSubgraph(State state, string rest, int line)
    {
        var model = state.Model;
        string id;
        string title;
        var bracket = rest.IndexOf('[');
        if (bracket > 0 && rest.EndsWith(']') && !rest[..bracket].Trim().Contains(' '))
        {
            id = rest[..bracket].Trim();
            title = ParseHelpers.Unquote(rest[(bracket + 1)..^1]);
        }
        else if (rest.Length >= 2 && rest[0] == '"' && rest[^1] == '"')
        {
            title = rest[1..^1];
            id = title;
        }
        else if (rest.Length > 0)
        {
            id = rest;
            title = rest;
        }
        else
        {
            id = "subGraph" + model.Subgraphs.Count;
            title = "";
        }

        var parent = state.Stack.Count > 0 ? state.Stack.Peek() : null;
        if (!model.Subgraphs.ContainsKey(id))
        {
            ParseHelpers.CheckCount(model.Subgraphs.Count + 1, MermaidEngine.MaxElements, line, "subgraphs");
            model.Subgraphs[id] = new FlowSubgraph(id, title, parent, model.Subgraphs.Count);
        }

        if (state.Stack.Count >= 32)
        {
            throw new MermaidParseException(MermaidErrorCode.TooDeep, "Subgraphs are nested too deeply.", line);
        }

        state.Stack.Push(id);
    }

    private static void LinkStyle(State state, string rest, int line)
    {
        var space = rest.IndexOfAny([' ', '\t']);
        if (space < 0)
        {
            throw ParseHelpers.Error(line, "linkStyle needs edge numbers and styles.");
        }

        var targets = rest[..space];
        var body = rest[space..].Trim();
        string? curve = null;
        if (ParseHelpers.StartsWithWord(body, "interpolate"))
        {
            var parts = body[11..].Trim().Split([' ', '\t'], 2);
            curve = parts[0];
            body = parts.Length > 1 ? parts[1] : "";
        }

        var style = Style.Parse(body);
        if (targets == "default")
        {
            state.Model.DefaultLinkStyle = state.Model.DefaultLinkStyle.Merge(style);
            state.Model.DefaultCurve = curve ?? state.Model.DefaultCurve;
            return;
        }

        foreach (var target in targets.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(target, out var index) || index < 0)
            {
                throw ParseHelpers.Error(line, $"Invalid linkStyle index '{Source.Truncate(target)}'.");
            }

            state.LinkStyles.Add((index, (style, curve)));
        }
    }

    private static FlowNode Node(State state, string id, int line)
    {
        var model = state.Model;
        if (!model.Nodes.TryGetValue(id, out var node))
        {
            ParseHelpers.CheckCount(model.Nodes.Count + 1, MermaidEngine.MaxElements, line, "nodes");
            node = new FlowNode(id, model.Nodes.Count);
            model.Nodes[id] = node;
        }

        if (state.Stack.Count > 0 && !model.Subgraphs.ContainsKey(id))
        {
            var current = state.Stack.Peek();
            if (node.Subgraph is null || IsAncestor(model, node.Subgraph, current))
            {
                node.Subgraph = current;
            }
        }

        return node;
    }

    private static bool IsAncestor(FlowchartModel model, string ancestor, string subgraph)
    {
        for (var s = model.Subgraphs[subgraph].Parent; s is not null; s = model.Subgraphs[s].Parent)
        {
            if (s == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    private static void Chain(State state, string text, int line)
    {
        var scanner = new Scanner(text, line);
        var previous = NodeGroup(state, scanner);
        while (!scanner.AtEnd)
        {
            var link = Link(scanner);
            var next = NodeGroup(state, scanner);
            var first = true;
            foreach (var a in previous)
            {
                foreach (var b in next)
                {
                    ParseHelpers.CheckCount(state.Model.Edges.Count + 1, MermaidEngine.MaxConnections, line, "edges");
                    var edge = new FlowEdge
                    {
                        From = a,
                        To = b,
                        Id = first ? link.Id : null,
                        Label = link.Label,
                        Line = link.Line,
                        Start = link.Start,
                        End = link.End,
                        Length = link.Length
                    };
                    if (edge.Id is not null && state.EdgeData.TryGetValue(edge.Id, out var data))
                    {
                        edge.Style = data.Style;
                        edge.Curve = data.Curve;
                    }

                    state.Model.Edges.Add(edge);
                    first = false;
                }
            }

            previous = next;
        }
    }

    private sealed class Scanner(string text, int line)
    {
        public string Text { get; } = text;

        public int Line { get; } = line;

        public int Pos { get; set; }

        public bool AtEnd
        {
            get
            {
                SkipSpaces();
                return Pos >= Text.Length;
            }
        }

        public string Rest => Text[Pos..];

        public void SkipSpaces()
        {
            while (Pos < Text.Length && char.IsWhiteSpace(Text[Pos]))
            {
                Pos++;
            }
        }

        public bool StartsWith(string s) => string.CompareOrdinal(Text, Pos, s, 0, s.Length) == 0;

        public MermaidParseException Error(string message) => new(MermaidErrorCode.Syntax, message, Line);
    }

    private static List<string> NodeGroup(State state, Scanner scanner)
    {
        var ids = new List<string> { NodeReference(state, scanner) };
        while (true)
        {
            scanner.SkipSpaces();
            if (!scanner.StartsWith("&"))
            {
                return ids;
            }

            scanner.Pos++;
            ids.Add(NodeReference(state, scanner));
        }
    }

    private static string NodeReference(State state, Scanner scanner)
    {
        scanner.SkipSpaces();
        var text = scanner.Text;
        var start = scanner.Pos;
        while (scanner.Pos < text.Length)
        {
            var c = text[scanner.Pos];
            if (ParseHelpers.IsIdChar(c))
            {
                scanner.Pos++;
                continue;
            }

            // Single dashes and dots may join id parts (a-b), but never start a link.
            if ((c == '-' || c == '.') && scanner.Pos + 1 < text.Length && ParseHelpers.IsIdChar(text[scanner.Pos + 1]) && scanner.Pos > start)
            {
                scanner.Pos++;
                continue;
            }

            break;
        }

        if (scanner.Pos == start)
        {
            throw scanner.Error($"Expected a node id at '{Source.Truncate(scanner.Rest)}'.");
        }

        var id = text[start..scanner.Pos];
        if (id is "end" or "subgraph")
        {
            throw scanner.Error($"'{id}' cannot be used as a node id.");
        }

        // Edge ids (e1@-->) look like node ids followed by '@'.
        if (scanner.Pos < text.Length && text[scanner.Pos] == '@' && scanner.Pos + 1 < text.Length && text[scanner.Pos + 1] is '-' or '=' or '.' or '~' or '<')
        {
            throw scanner.Error("An edge id must follow a node.");
        }

        var node = Node(state, id, scanner.Line);
        if (scanner.StartsWith("@{"))
        {
            var close = FindClosingBrace(text, scanner.Pos + 1);
            if (close < 0)
            {
                throw scanner.Error("Unclosed '@{'.");
            }

            ApplyShapeData(state, node, id, text[(scanner.Pos + 2)..close], scanner);
            scanner.Pos = close + 1;
        }
        else
        {
            ReadShape(scanner, node);
        }

        if (scanner.StartsWith(":::"))
        {
            scanner.Pos += 3;
            var classStart = scanner.Pos;
            while (scanner.Pos < text.Length && (ParseHelpers.IsIdChar(text[scanner.Pos]) || text[scanner.Pos] == '-'))
            {
                scanner.Pos++;
            }

            node.Classes.Add(text[classStart..scanner.Pos]);
        }

        return id;
    }

    private static int FindClosingBrace(string text, int open)
    {
        var quote = '\0';
        for (var i = open + 1; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '}')
            {
                return i;
            }
        }

        return -1;
    }

    private static void ApplyShapeData(State state, FlowNode node, string id, string body, Scanner scanner)
    {
        var map = ParseHelpers.InlineMap(body);
        if (map.TryGetValue("shape", out var shapeName))
        {
            node.Shape = Shapes.FromName(shapeName) ?? throw scanner.Error($"Unknown shape '{Source.Truncate(shapeName)}'.");
            node.Explicit = true;
        }

        if (map.TryGetValue("label", out var label))
        {
            node.Label = label;
            node.Explicit = true;
        }

        // Edge data: e1@{ curve: linear }
        if (map.TryGetValue("curve", out var curve) || map.ContainsKey("animate") || map.ContainsKey("animation"))
        {
            state.EdgeData[id] = (Style.None, curve);
            foreach (var edge in state.Model.Edges.Where(e => e.Id == id))
            {
                edge.Curve = curve ?? edge.Curve;
            }

            if (!node.Explicit)
            {
                state.Model.Nodes.Remove(id);
            }
        }
    }

    private static void ReadShape(Scanner scanner, FlowNode node)
    {
        if (!ReadShapeAt(scanner.Text, scanner.Pos, out var label, out var shape, out var next, out var error))
        {
            return;
        }

        if (error is not null)
        {
            throw scanner.Error(error);
        }

        node.Label = label;
        node.Shape = shape;
        node.Explicit = true;
        scanner.Pos = next;
    }

    /// <summary>
    /// Reads a flowchart node shape such as <c>[text]</c> or <c>((text))</c> at <paramref name="pos"/>.
    /// Returns false when no shape opens there; <paramref name="error"/> is set when one opens but is malformed.
    /// </summary>
    internal static bool ReadShapeAt(string text, int pos, out string label, out NodeShape shapeOut, out int next, out string? error)
    {
        label = "";
        shapeOut = NodeShape.Rect;
        next = pos;
        error = null;
        foreach (var (open, close, shape, altClose, altShape) in Openers)
        {
            if (string.CompareOrdinal(text, pos, open, 0, open.Length) != 0)
            {
                continue;
            }

            var contentStart = pos + open.Length;
            int end;
            var actualShape = shape;
            var actualClose = close;
            var p = contentStart;
            while (p < text.Length && text[p] == ' ')
            {
                p++;
            }

            if (p < text.Length && text[p] == '"')
            {
                var quoteEnd = text.IndexOf('"', p + 1);
                if (quoteEnd < 0)
                {
                    error = "Unclosed quote in node text.";
                    return true;
                }

                label = text[(p + 1)..quoteEnd];
                var q = quoteEnd + 1;
                while (q < text.Length && text[q] == ' ')
                {
                    q++;
                }

                if (string.CompareOrdinal(text, q, close, 0, close.Length) == 0)
                {
                    end = q;
                }
                else if (altClose is not null && string.CompareOrdinal(text, q, altClose, 0, altClose.Length) == 0)
                {
                    end = q;
                    actualShape = altShape;
                    actualClose = altClose;
                }
                else
                {
                    error = $"Expected '{close}' after node text.";
                    return true;
                }
            }
            else
            {
                end = text.IndexOf(close, contentStart, StringComparison.Ordinal);
                if (altClose is not null)
                {
                    var alt = text.IndexOf(altClose, contentStart, StringComparison.Ordinal);
                    if (alt >= 0 && (end < 0 || alt < end))
                    {
                        end = alt;
                        actualShape = altShape;
                        actualClose = altClose;
                    }
                }

                if (end < 0)
                {
                    error = $"Expected '{close}' to close the node text.";
                    return true;
                }

                label = text[contentStart..end].Trim();
            }

            shapeOut = actualShape;
            next = end + actualClose.Length;
            return true;
        }

        return false;
    }

    private sealed record LinkInfo(string? Id, string? Label, LineKind Line, Marker Start, Marker End, int Length);

    private static LinkInfo Link(Scanner scanner)
    {
        scanner.SkipSpaces();
        string? id = null;
        var idMatch = EdgeId().Match(scanner.Rest);
        if (idMatch.Success)
        {
            id = idMatch.Groups["id"].Value;
            scanner.Pos += idMatch.Length;
        }

        var rest = scanner.Rest;
        var textOpen = TextLinkOpen().Match(rest);
        if (textOpen.Success)
        {
            var opener = textOpen.Groups["o"].Value;
            var closer = opener switch
            {
                "--" => new Regex(@"\s(?<l>-{2,})(?<e>[>xo])?(?=\s|$|[\p{L}\p{N}_""])", RegexOptions.CultureInvariant),
                "==" => new Regex(@"\s(?<l>={2,})(?<e>[>xo])?(?=\s|$|[\p{L}\p{N}_""])", RegexOptions.CultureInvariant),
                _ => new Regex(@"\s(?<l>\.+-)(?<e>[>xo])?(?=\s|$|[\p{L}\p{N}_""])", RegexOptions.CultureInvariant)
            };
            var match = closer.Match(rest, textOpen.Length - 1);
            if (match.Success && (match.Groups["e"].Success || match.Groups["l"].Value.Length >= 3 || opener == "-."))
            {
                var label = rest[textOpen.Length..match.Index].Trim();
                scanner.Pos += match.Index + match.Length;
                var line = opener switch { "==" => LineKind.Thick, "-." => LineKind.Dotted, _ => LineKind.Normal };
                var end = HeadMarker(match.Groups["e"].Value);
                var lineChars = match.Groups["l"].Value.Length;
                var length = line == LineKind.Dotted ? Math.Max(1, lineChars - 1) : Math.Max(1, lineChars - (end == Marker.None ? 2 : 1));
                return new LinkInfo(id, ParseHelpers.Unquote(label), line, HeadMarker(textOpen.Groups["s"].Value, start: true), end, length);
            }
        }

        var normal = NormalLink().Match(rest);
        if (!normal.Success)
        {
            throw scanner.Error($"Expected a link at '{Source.Truncate(rest.Trim())}'.");
        }

        var l = normal.Groups["l"].Value;
        var e = normal.Groups["e"].Value;
        var consumed = normal.Length;
        // 'x' or 'o' directly followed by an id character starts the next node, not an arrowhead.
        if (e is "x" or "o" && consumed < rest.Length && ParseHelpers.IsIdChar(rest[consumed]))
        {
            e = "";
            consumed--;
        }

        var kind = l[0] switch
        {
            '=' => LineKind.Thick,
            '~' => LineKind.Invisible,
            _ => l.Contains('.') ? LineKind.Dotted : LineKind.Normal
        };
        if (e.Length == 0 && (l == "--" || l == "=="))
        {
            throw scanner.Error("Incomplete link: use '---' or '-->'.");
        }

        var endMarker = kind == LineKind.Invisible ? Marker.None : HeadMarker(e);
        var startMarker = kind == LineKind.Invisible ? Marker.None : HeadMarker(normal.Groups["s"].Value, start: true);
        var len = kind switch
        {
            LineKind.Dotted => Math.Max(1, l.Count(c => c == '.')),
            LineKind.Invisible => Math.Max(1, l.Length - 2),
            _ => Math.Max(1, l.Length - (endMarker == Marker.None ? 2 : 1))
        };
        scanner.Pos += consumed;
        string? pipe = null;
        var pipeMatch = PipeLabel().Match(scanner.Rest);
        if (pipeMatch.Success)
        {
            pipe = ParseHelpers.Unquote(pipeMatch.Groups["t"].Value);
            scanner.Pos += pipeMatch.Length;
        }

        return new LinkInfo(id, pipe, kind, startMarker, endMarker, len);
    }

    private static Marker HeadMarker(string head, bool start = false) => head switch
    {
        ">" => Marker.Arrow,
        "<" when start => Marker.Arrow,
        "x" => Marker.Cross,
        "o" => Marker.Circle,
        _ => Marker.None
    };
}
