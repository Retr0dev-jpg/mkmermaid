using System.Text;
using System.Text.RegularExpressions;
using MkMermaid.Parsing;
using MkMermaid.Rendering;
using static MkMermaid.Parsing.ParseHelpers;

namespace MkMermaid.Diagrams.Sequence;

/// <summary>
/// ZenUML onto the sequence model: sync calls with bodies become activations, assignments and <c>return</c>
/// become dashed return messages, and if/while/try/opt/par become fragments.
/// </summary>
internal static partial class ZenUmlParser
{
    private const int MaxDepth = 32;

    private enum TokenKind
    {
        Statement,
        Open,
        Close
    }

    private sealed record Token(TokenKind Kind, string Text, int Line);

    private sealed class CallFrame(string caller)
    {
        public string Caller { get; } = caller;

        public bool Returned { get; set; }
    }

    private sealed class State(SequenceModel model, List<Token> tokens)
    {
        public SequenceModel Model { get; } = model;

        public List<Token> Tokens { get; } = tokens;

        public int Index { get; set; }

        public int Depth { get; set; }

        /// <summary>Who called each active participant, innermost last, so <c>return</c> knows where to go.</summary>
        public Dictionary<string, Stack<CallFrame>> Callers { get; } = new(StringComparer.Ordinal);

        public bool Next(TokenKind kind) => Index < Tokens.Count && Tokens[Index].Kind == kind;
    }

    [GeneratedRegex(@"^@(?<kind>\w+)\s+(?<name>""[^""]+""|[\w.]+)(?:\s+as\s+(?<label>.+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex Annotated();

    [GeneratedRegex(@"^(?<kw>if|while|for|forEach|foreach|loop|opt|par|critical|break|section|try)\b\s*(?:\((?<cond>.*)\))?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex BlockKeyword();

    [GeneratedRegex(@"^(?:(?<type>[\w<>\[\],]+)\s+)?(?<var>\w+)\s*=\s*(?<expr>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex Assignment();

    [GeneratedRegex(@"^new\s+(?<name>\w+)\s*(?:\((?<args>.*)\))?$", RegexOptions.CultureInvariant)]
    private static partial Regex Creation();

    [GeneratedRegex(@"^(?:(?<from>\w+)\s*->\s*)?(?<to>\w+)\s*\.\s*(?<method>[\w.]+\s*(?:\(.*\))?)$", RegexOptions.CultureInvariant)]
    private static partial Regex SyncCall();

    [GeneratedRegex(@"^(?<from>\w+)\s*->\s*(?<to>\w+)\s*:\s*(?<text>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex AsyncMessage();

    [GeneratedRegex(@"^(?<name>""[^""]+""|\w+)(?:\s+as\s+(?<label>.+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex Declaration();

    public static SequenceModel Parse(Source source)
    {
        var state = new State(new SequenceModel(), Tokenize(source));
        Block(state, caller: null, insideBody: false);
        return state.Model;
    }

    /// <summary>Splits lines into statements and braces; braces and semicolons inside quotes or parentheses do not count.</summary>
    private static List<Token> Tokenize(Source source)
    {
        var tokens = new List<Token>();
        foreach (var line in source.Lines)
        {
            var text = line.Trimmed;
            var comment = text.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0 && text[..comment].Count(c => c == '"') % 2 == 0)
            {
                text = text[..comment].TrimEnd();
            }

            if (StartsWithWord(text, "title"))
            {
                tokens.Add(new Token(TokenKind.Statement, text, line.Number));
                continue;
            }

            var current = new StringBuilder();
            var depth = 0;
            var quote = false;
            foreach (var c in text)
            {
                if (c == '"')
                {
                    quote = !quote;
                }
                else if (!quote && c == '(')
                {
                    depth++;
                }
                else if (!quote && c == ')')
                {
                    depth = Math.Max(0, depth - 1);
                }
                else if (!quote && depth == 0 && c is '{' or '}' or ';')
                {
                    Flush(tokens, current, line.Number);
                    if (c != ';')
                    {
                        tokens.Add(new Token(c == '{' ? TokenKind.Open : TokenKind.Close, c.ToString(), line.Number));
                    }

                    continue;
                }

                current.Append(c);
            }

            Flush(tokens, current, line.Number);
        }

        return tokens;
    }

    private static void Flush(List<Token> tokens, StringBuilder current, int line)
    {
        var statement = current.ToString().Trim();
        if (statement.Length > 0)
        {
            tokens.Add(new Token(TokenKind.Statement, statement, line));
        }

        current.Clear();
    }

    /// <summary>Parses statements until the matching close brace (or the end). A null caller means "the starter".</summary>
    private static void Block(State state, string? caller, bool insideBody)
    {
        if (insideBody && ++state.Depth > MaxDepth)
        {
            var line = state.Tokens[Math.Min(state.Index, state.Tokens.Count) - 1].Line;
            throw new MermaidParseException(MermaidErrorCode.TooDeep, $"Blocks are nested more than {MaxDepth} levels deep.", line);
        }

        while (state.Index < state.Tokens.Count)
        {
            var token = state.Tokens[state.Index++];
            switch (token.Kind)
            {
                case TokenKind.Close when insideBody:
                    state.Depth--;
                    return;
                case TokenKind.Close:
                    throw Error(token.Line, "Unexpected '}'.");
                case TokenKind.Open:
                    Block(state, caller, insideBody: true);
                    break;
                default:
                    Statement(state, token, caller);
                    break;
            }
        }

        if (insideBody)
        {
            throw Error(state.Tokens.Count == 0 ? 0 : state.Tokens[^1].Line, "Missing '}'.");
        }
    }

    private static void Statement(State state, Token token, string? caller)
    {
        var model = state.Model;
        var text = token.Text;
        var line = token.Line;
        CheckCount(model.Events.Count + model.Participants.Count + 1, MermaidEngine.MaxElements, line, "statements");
        if (StartsWithWord(text, "title"))
        {
            model.Title = text[5..].Trim();
            return;
        }

        if (Annotated().Match(text) is { Success: true } annotated)
        {
            var participant = model.Ensure(Unquote(annotated.Groups["name"].Value));
            participant.Kind = Kind(annotated.Groups["kind"].Value);
            if (annotated.Groups["label"].Success)
            {
                participant.Label = Unquote(annotated.Groups["label"].Value);
            }

            return;
        }

        if (StartsWithWord(text, "group"))
        {
            Group(state, text[5..].Trim(), line);
            return;
        }

        if (BlockKeyword().Match(text) is { Success: true } keyword)
        {
            Fragment(state, keyword.Groups["kw"].Value, keyword.Groups["cond"].Success ? keyword.Groups["cond"].Value.Trim() : "", caller, line);
            return;
        }

        if (StartsWithWord(text, "return"))
        {
            if (caller is not null && state.Callers.TryGetValue(caller, out var callers) && callers.Count > 0)
            {
                var frame = callers.Peek();
                frame.Returned = true;
                model.Events.Add(new MessageEvent(line, caller, frame.Caller, true, Marker.Arrow, false, text[6..].Trim(), false, false));
            }

            return;
        }

        if (Assignment().Match(text) is { Success: true } assignment && Call(state, assignment.Groups["expr"].Value.Trim(), caller, line, assignment.Groups["var"].Value))
        {
            return;
        }

        if (Call(state, text, caller, line, null))
        {
            return;
        }

        if (AsyncMessage().Match(text) is { Success: true } message)
        {
            var from = model.Ensure(message.Groups["from"].Value).Id;
            var to = model.Ensure(message.Groups["to"].Value).Id;
            model.Events.Add(new MessageEvent(line, from, to, false, Marker.OpenArrow, false, message.Groups["text"].Value.Trim(), false, false));
            return;
        }

        if (Declaration().Match(text) is { Success: true } declaration)
        {
            var participant = model.Ensure(Unquote(declaration.Groups["name"].Value));
            if (declaration.Groups["label"].Success)
            {
                participant.Label = Unquote(declaration.Groups["label"].Value);
            }

            return;
        }

        throw Error(line, $"Unrecognized statement '{Source.Truncate(text)}'.");
    }

    private static void Group(State state, string label, int line)
    {
        Expect(state, TokenKind.Open, line);
        var box = state.Model.Boxes.Count;
        state.Model.Boxes.Add(new SequenceBox(label.Length == 0 ? null : Unquote(label), null));
        while (state.Next(TokenKind.Statement))
        {
            foreach (var name in state.Tokens[state.Index].Text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries))
            {
                state.Model.Ensure(Unquote(name)).Box = box;
            }

            state.Index++;
        }

        Expect(state, TokenKind.Close, line);
    }

    private static void Fragment(State state, string keyword, string condition, string? caller, int line)
    {
        var kind = keyword switch
        {
            "if" or "try" => BlockKind.Alt,
            "opt" => BlockKind.Opt,
            "par" => BlockKind.Par,
            "critical" or "section" => BlockKind.Critical,
            "break" => BlockKind.Break,
            _ => BlockKind.Loop
        };
        state.Model.Events.Add(new BlockStartEvent(line, kind, keyword == "try" ? "try" : condition));
        Body(state, caller, line);
        while (state.Next(TokenKind.Statement) && Continuation(state.Tokens[state.Index].Text, keyword) is { } divider)
        {
            state.Model.Events.Add(new BlockDividerEvent(state.Tokens[state.Index].Line, divider));
            state.Index++;
            Body(state, caller, line);
        }

        state.Model.Events.Add(new BlockEndEvent(line));
    }

    private static bool Call(State state, string text, string? caller, int line, string? assignTo)
    {
        var model = state.Model;
        string target;
        string label;
        string? from = null;
        var create = false;
        if (Creation().Match(text) is { Success: true } creation)
        {
            target = creation.Groups["name"].Value;
            label = "«create»";
            create = true;
        }
        else if (SyncCall().Match(text) is { Success: true } call)
        {
            target = call.Groups["to"].Value;
            label = call.Groups["method"].Value.Trim();
            from = call.Groups["from"].Success ? call.Groups["from"].Value : null;
        }
        else
        {
            return false;
        }

        var source = model.Ensure(from ?? caller ?? Starter(model)).Id;
        if (create && !model.Participants.ContainsKey(target))
        {
            model.Ensure(target).Created = true;
            model.Events.Add(new CreateEvent(line, target));
        }
        else
        {
            model.Ensure(target);
        }

        var hasBody = state.Next(TokenKind.Open);
        var activates = hasBody || assignTo is not null;
        model.Events.Add(new MessageEvent(line, source, target, false, Marker.Arrow, false, label, activates, false));
        var returned = false;
        if (hasBody)
        {
            if (!state.Callers.TryGetValue(target, out var callers))
            {
                state.Callers[target] = callers = new Stack<CallFrame>();
            }

            callers.Push(new CallFrame(source));
            state.Index++;
            Block(state, target, insideBody: true);
            returned = callers.Pop().Returned;
        }

        if (assignTo is not null && !returned)
        {
            model.Events.Add(new MessageEvent(line, target, source, true, Marker.Arrow, false, assignTo, false, true));
        }
        else if (activates)
        {
            model.Events.Add(new ActivationEvent(line, target, false));
        }

        return true;
    }

    /// <summary>Calls without an explicit sender come from the first declared participant, like ZenUML's starter.</summary>
    private static string Starter(SequenceModel model) =>
        model.Participants.Values.OrderBy(p => p.Order).FirstOrDefault()?.Id ?? "Client";

    private static void Body(State state, string? caller, int line)
    {
        Expect(state, TokenKind.Open, line);
        Block(state, caller, insideBody: true);
    }

    private static string? Continuation(string text, string keyword)
    {
        if (keyword == "if" && StartsWithWord(text, "else"))
        {
            var rest = text[4..].Trim();
            return rest.Length == 0 ? "else" : StartsWithWord(rest, "if") ? rest[2..].Trim().TrimStart('(').TrimEnd(')').Trim() : null;
        }

        if (keyword == "try" && StartsWithWord(text, "catch"))
        {
            return ("catch " + text[5..].Trim().TrimStart('(').TrimEnd(')').Trim()).Trim();
        }

        return keyword == "try" && text == "finally" ? "finally" : null;
    }

    private static void Expect(State state, TokenKind kind, int line)
    {
        if (!state.Next(kind))
        {
            throw Error(state.Index < state.Tokens.Count ? state.Tokens[state.Index].Line : line, kind == TokenKind.Open ? "Expected '{'." : "Expected '}'.");
        }

        state.Index++;
    }

    private static ParticipantKind Kind(string annotation) => annotation.ToLowerInvariant() switch
    {
        "actor" => ParticipantKind.Actor,
        "boundary" => ParticipantKind.Boundary,
        "control" => ParticipantKind.Control,
        "entity" => ParticipantKind.Entity,
        "database" => ParticipantKind.Database,
        "collections" or "collection" => ParticipantKind.Collections,
        "queue" => ParticipantKind.Queue,
        _ => ParticipantKind.Participant
    };
}
