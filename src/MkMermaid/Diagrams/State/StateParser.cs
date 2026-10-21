using System.Text.RegularExpressions;
using MkMermaid.Diagrams.Flowchart;
using MkMermaid.Parsing;
using MkMermaid.Rendering;
using static MkMermaid.Parsing.ParseHelpers;

namespace MkMermaid.Diagrams.State;

internal static partial class StateParser
{
    private const int MaxDepth = 32;

    [GeneratedRegex(@"^(?<a>\[\*\]|[^\s:\-]+(?:-[^\s:\->]+)*)(?<ac>:::[\w\-]+)?\s*-->\s*(?<b>\[\*\]|[^\s:]+?)(?<bc>:::[\w\-]+)?\s*(?::\s*(?<label>.*))?$", RegexOptions.CultureInvariant)]
    private static partial Regex TransitionLine();

    [GeneratedRegex(@"^state\s+""(?<label>[^""]*)""\s+as\s+(?<id>[^\s{:]+)\s*(?<open>\{)?$", RegexOptions.CultureInvariant)]
    private static partial Regex AliasLine();

    [GeneratedRegex(@"^state\s+(?<id>[^\s{:<]+)\s*(?:<<(?<type>fork|join|choice)>>)?\s*(?<classes>(?::::[\w\-]+)*)\s*(?<open>\{)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StateLine();

    [GeneratedRegex(@"^note\s+(?<side>left|right)\s+of\s+(?<id>[^\s:]+)\s*(?::\s*(?<text>.*))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NoteLine();

    [GeneratedRegex(@"^(?<id>[^\s:]+)\s*:\s*(?<text>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex DescriptionLine();

    private sealed record Scope(string? Composite, string? Cluster);

    public static StateModel Parse(Source source)
    {
        var model = new StateModel();
        var scopes = new Stack<Scope>();
        scopes.Push(new Scope(null, null));
        (StateNode Node, bool Left, List<string> Lines)? openNote = null;
        for (var i = 0; i < source.Lines.Count; i++)
        {
            var line = source.Lines[i];
            var text = line.Trimmed;
            var scope = scopes.Peek();
            if (openNote is { } note)
            {
                if (text.Equals("end note", StringComparison.OrdinalIgnoreCase))
                {
                    note.Node.Note = new StateNote(note.Left, string.Join("\n", note.Lines));
                    openNote = null;
                }
                else
                {
                    note.Lines.Add(text);
                }

                continue;
            }

            if (text == "}")
            {
                if (scopes.Count == 1)
                {
                    throw Error(line, "'}' without an open composite state.");
                }

                scopes.Pop();
                continue;
            }

            if (text == "--")
            {
                if (scope.Composite is null)
                {
                    throw Error(line, "'--' separates regions inside a composite state.");
                }

                var composite = model.States[scope.Composite];
                if (composite.Regions.Count == 0)
                {
                    AddRegion(model, composite);
                    ReassignToRegion(model, composite);
                }

                scopes.Pop();
                scopes.Push(new Scope(composite.Id, AddRegion(model, composite)));
                continue;
            }

            var word = text.Split([' ', '\t'], 2)[0];
            var rest = text.Length > word.Length ? text[word.Length..].Trim() : "";
            switch (word)
            {
                case "direction":
                    var direction = FlowchartModel.ParseDirection(rest) ?? throw Error(line, $"Unknown direction '{Source.Truncate(rest)}'.");
                    if (scopes.Count == 1)
                    {
                        model.Direction = direction;
                    }

                    continue;
                case "hide" or "scale" or "click":
                    continue;
                case "classDef":
                    var parts = rest.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        model.ClassDefs[parts[0]] = Style.Parse(parts[1]);
                    }

                    continue;
                case "class":
                    var classParts = rest.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
                    if (classParts.Length == 2)
                    {
                        foreach (var id in classParts[0].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                        {
                            model.Ensure(id, scope.Cluster).Classes.Add(classParts[1].Trim());
                        }
                    }

                    continue;
                case "style":
                    var styleParts = rest.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
                    if (styleParts.Length == 2)
                    {
                        foreach (var id in styleParts[0].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                        {
                            var node = model.Ensure(id, scope.Cluster);
                            node.Style = (node.Style ?? Style.None).Merge(Style.Parse(styleParts[1]));
                        }
                    }

                    continue;
            }

            if (NoteLine().Match(text) is { Success: true } n)
            {
                var target = model.Ensure(n.Groups["id"].Value, scope.Cluster);
                var left = n.Groups["side"].Value.Equals("left", StringComparison.OrdinalIgnoreCase);
                if (n.Groups["text"].Success)
                {
                    target.Note = new StateNote(left, n.Groups["text"].Value.Trim());
                }
                else
                {
                    openNote = (target, left, []);
                }

                continue;
            }

            if (AliasLine().Match(text) is { Success: true } alias)
            {
                var node = model.Ensure(alias.Groups["id"].Value, scope.Cluster);
                node.Label = alias.Groups["label"].Value;
                if (alias.Groups["open"].Success)
                {
                    Open(model, scopes, node, line);
                }

                continue;
            }

            if (StateLine().Match(text) is { Success: true } state)
            {
                var node = model.Ensure(state.Groups["id"].Value, scope.Cluster);
                if (state.Groups["type"].Success)
                {
                    node.Type = state.Groups["type"].Value.ToLowerInvariant() switch
                    {
                        "fork" => StateType.Fork,
                        "join" => StateType.Join,
                        _ => StateType.Choice
                    };
                }

                foreach (var c in state.Groups["classes"].Value.Split(":::", StringSplitOptions.RemoveEmptyEntries))
                {
                    node.Classes.Add(c);
                }

                if (state.Groups["open"].Success)
                {
                    Open(model, scopes, node, line);
                }

                continue;
            }

            if (TransitionLine().Match(text) is { Success: true } t)
            {
                CheckCount(model.Transitions.Count + 1, MermaidEngine.MaxConnections, line.Number, "transitions");
                var from = Endpoint(model, t.Groups["a"].Value, scope, start: true);
                var to = Endpoint(model, t.Groups["b"].Value, scope, start: false);
                AddClasses(model, from, t.Groups["ac"].Value);
                AddClasses(model, to, t.Groups["bc"].Value);
                model.Transitions.Add(new StateTransition(from, to, t.Groups["label"].Success ? t.Groups["label"].Value.Trim() : null));
                continue;
            }

            if (DescriptionLine().Match(text) is { Success: true } d)
            {
                model.Ensure(d.Groups["id"].Value, scope.Cluster).Descriptions.Add(d.Groups["text"].Value.Trim());
                continue;
            }

            if (!text.Contains(' ', StringComparison.Ordinal) && !text.Contains('-', StringComparison.Ordinal))
            {
                model.Ensure(text, scope.Cluster);
                continue;
            }

            throw Error(line, $"Unrecognized statement '{Source.Truncate(text)}'.");
        }

        if (openNote is not null)
        {
            throw new MermaidParseException(MermaidErrorCode.UnclosedBlock, "A note is not closed with 'end note'.", source.Lines[^1].Number);
        }

        if (scopes.Count > 1)
        {
            throw new MermaidParseException(MermaidErrorCode.UnclosedBlock, $"State '{Source.Truncate(scopes.Peek().Composite ?? "")}' is not closed with '}}'.", source.Lines[^1].Number);
        }

        CheckCount(model.States.Count, MermaidEngine.MaxElements, source.HeaderLine, "states");
        return model;
    }

    private static void Open(StateModel model, Stack<Scope> scopes, StateNode node, SourceLine line)
    {
        if (scopes.Count > MaxDepth)
        {
            throw new MermaidParseException(MermaidErrorCode.TooDeep, $"States are nested more than {MaxDepth} levels deep.", line.Number);
        }

        for (var s = scopes.Peek().Composite; s is not null; s = model.States[s].Scope is { } c ? model.RegionParents.GetValueOrDefault(c, c) : null)
        {
            if (s == node.Id)
            {
                throw Error(line, $"State '{Source.Truncate(node.Id)}' cannot contain itself.");
            }
        }

        node.Composite = true;
        scopes.Push(new Scope(node.Id, node.Regions.Count > 0 ? node.Regions[^1] : node.Id));
    }

    private static string AddRegion(StateModel model, StateNode composite)
    {
        var id = composite.Id + "\u0001region" + composite.Regions.Count;
        composite.Regions.Add(id);
        model.RegionParents[id] = composite.Id;
        return id;
    }

    /// <summary>States already placed directly in a composite move into its first region once a second region appears.</summary>
    private static void ReassignToRegion(StateModel model, StateNode composite)
    {
        foreach (var state in model.States.Values.Where(s => s.Scope == composite.Id))
        {
            state.Scope = composite.Regions[0];
        }
    }

    private static string Endpoint(StateModel model, string raw, Scope scope, bool start)
    {
        if (raw != "[*]")
        {
            model.Ensure(raw, scope.Cluster);
            return raw;
        }

        var id = "\u0001" + (start ? "start" : "end") + ":" + (scope.Cluster ?? "");
        model.Ensure(id, scope.Cluster).Type = start ? StateType.Start : StateType.End;
        return id;
    }

    private static void AddClasses(StateModel model, string id, string classes)
    {
        foreach (var c in classes.Split(":::", StringSplitOptions.RemoveEmptyEntries))
        {
            model.States[id].Classes.Add(c);
        }
    }
}
