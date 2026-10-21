using System.Text;
using System.Text.RegularExpressions;
using MkMermaid.Diagrams.Flowchart;
using MkMermaid.Parsing;
using MkMermaid.Rendering;
using static MkMermaid.Parsing.ParseHelpers;

namespace MkMermaid.Diagrams.Class;

internal static partial class ClassParser
{
    private const int MaxDepth = 32;

    [GeneratedRegex(@"^(?<a>[A-Za-z0-9_\-.$`]+(?:~[^~]*~)?)\s*(?:""(?<ca>[^""]*)""\s*)?(?<left><\||\*|o|<|\(\))?(?<line>--|\.\.)(?<right>\|>|\*|o|>|\(\))?\s*(?:""(?<cb>[^""]*)""\s*)?(?<b>[A-Za-z0-9_\-.$`]+(?:~[^~]*~)?)\s*(?::\s*(?<label>.*))?$", RegexOptions.CultureInvariant)]
    private static partial Regex RelationLine();

    [GeneratedRegex(@"^class\s+(?<id>[A-Za-z0-9_\-.$`]+)(?:~(?<generic>.+?)~)?(?:\[""(?<label>[^""]*)""\])?(?<classes>(?::::[A-Za-z0-9_\-]+)*)\s*(?<open>\{)?\s*(?<inline>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex ClassLine();

    [GeneratedRegex(@"^<<(?<annotation>[^>]+)>>\s*(?<id>[A-Za-z0-9_\-.$`]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex AnnotationLine();

    [GeneratedRegex(@"^(?<id>[A-Za-z0-9_\-.$`]+)\s*:\s*(?<member>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex MemberLine();

    [GeneratedRegex(@"^note\s+(?:for\s+(?<for>[A-Za-z0-9_\-.$`]+)\s+)?""(?<text>.*)""$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NoteLine();

    public static ClassModel Parse(Source source)
    {
        var model = new ClassModel();
        var namespaces = new Stack<string>();
        ClassNode? body = null;
        foreach (var line in source.Lines)
        {
            var text = line.Trimmed;
            if (body is not null)
            {
                if (text.StartsWith('}'))
                {
                    body = null;
                    continue;
                }

                AddMemberOrAnnotation(body, text);
                continue;
            }

            if (text == "}")
            {
                if (!namespaces.TryPop(out _))
                {
                    throw Error(line, "'}' without an open block.");
                }

                continue;
            }

            var word = text.Split([' ', '\t'], 2)[0];
            var rest = text.Length > word.Length ? text[word.Length..].Trim() : "";
            switch (word)
            {
                case "direction":
                    model.Direction = FlowchartModel.ParseDirection(rest) ?? throw Error(line, $"Unknown direction '{Source.Truncate(rest)}'.");
                    continue;
                case "namespace":
                    var name = rest.TrimEnd('{').Trim();
                    if (name.Length == 0 || !rest.EndsWith('{'))
                    {
                        throw Error(line, "Use 'namespace Name {'.");
                    }

                    if (namespaces.Count >= MaxDepth)
                    {
                        throw new MermaidParseException(MermaidErrorCode.TooDeep, $"Namespaces are nested more than {MaxDepth} levels deep.", line.Number);
                    }

                    model.Namespaces[name] = namespaces.Count > 0 ? namespaces.Peek() : null;
                    namespaces.Push(name);
                    continue;
                case "classDef":
                    var parts = rest.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        foreach (var id in parts[0].Split(','))
                        {
                            model.ClassDefs[id.Trim()] = Style.Parse(parts[1]);
                        }
                    }

                    continue;
                case "cssClass":
                    var css = Regex.Match(rest, "^\"(?<ids>[^\"]+)\"\\s+(?<name>\\S+)$");
                    if (css.Success)
                    {
                        foreach (var id in css.Groups["ids"].Value.Split(','))
                        {
                            model.Ensure(Clean(id.Trim())).Classes.Add(css.Groups["name"].Value);
                        }
                    }

                    continue;
                case "style":
                    var styleParts = rest.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
                    if (styleParts.Length == 2)
                    {
                        var node = model.Ensure(Clean(styleParts[0]));
                        node.Style = (node.Style ?? Style.None).Merge(Style.Parse(styleParts[1]));
                    }

                    continue;
                case "click" or "callback" or "link":
                    continue;
            }

            if (NoteLine().Match(text) is { Success: true } note)
            {
                var target = note.Groups["for"].Success ? Clean(note.Groups["for"].Value) : null;
                if (target is not null)
                {
                    model.Ensure(target);
                }

                model.Notes.Add(new ClassNote(note.Groups["text"].Value.Replace("\\n", "\n", StringComparison.Ordinal), target));
                continue;
            }

            if (ClassLine().Match(text) is { Success: true } cls)
            {
                var node = Declare(model, cls.Groups["id"].Value, namespaces);
                if (cls.Groups["generic"].Success)
                {
                    node.Generic = Generic(cls.Groups["generic"].Value);
                }

                if (cls.Groups["label"].Success)
                {
                    node.Label = cls.Groups["label"].Value;
                }

                foreach (var c in cls.Groups["classes"].Value.Split(":::", StringSplitOptions.RemoveEmptyEntries))
                {
                    node.Classes.Add(c);
                }

                if (cls.Groups["open"].Success)
                {
                    var inline = cls.Groups["inline"].Value.Trim();
                    var close = inline.EndsWith('}');
                    foreach (var item in (close ? inline[..^1] : inline).Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    {
                        AddMemberOrAnnotation(node, item);
                    }

                    body = close ? null : node;
                }
                else if (cls.Groups["inline"].Value.Length > 0)
                {
                    throw Error(line, $"Unexpected text after class '{Source.Truncate(node.Id)}'.");
                }

                continue;
            }

            if (AnnotationLine().Match(text) is { Success: true } annotation)
            {
                model.Ensure(Clean(annotation.Groups["id"].Value)).Annotations.Add(annotation.Groups["annotation"].Value.Trim());
                continue;
            }

            if (RelationLine().Match(text) is { Success: true } relation)
            {
                CheckCount(model.Relations.Count + 1, MermaidEngine.MaxConnections, line.Number, "relations");
                var a = Declare(model, relation.Groups["a"].Value, namespaces);
                var b = Declare(model, relation.Groups["b"].Value, namespaces);
                model.Relations.Add(new ClassRelation(
                    a.Id, b.Id,
                    MarkerFor(relation.Groups["left"].Value), MarkerFor(relation.Groups["right"].Value),
                    relation.Groups["line"].Value == "..",
                    relation.Groups["label"].Success ? relation.Groups["label"].Value.Trim() : null,
                    relation.Groups["ca"].Success ? relation.Groups["ca"].Value : null,
                    relation.Groups["cb"].Success ? relation.Groups["cb"].Value : null));
                continue;
            }

            if (MemberLine().Match(text) is { Success: true } member)
            {
                AddMemberOrAnnotation(model.Ensure(Clean(member.Groups["id"].Value)), member.Groups["member"].Value.Trim());
                continue;
            }

            throw Error(line, $"Unrecognized statement '{Source.Truncate(text)}'.");
        }

        if (body is not null)
        {
            throw new MermaidParseException(MermaidErrorCode.UnclosedBlock, $"Class '{Source.Truncate(body.Id)}' is not closed with '}}'.", source.Lines[^1].Number);
        }

        if (namespaces.Count > 0)
        {
            throw new MermaidParseException(MermaidErrorCode.UnclosedBlock, $"Namespace '{Source.Truncate(namespaces.Peek())}' is not closed with '}}'.", source.Lines[^1].Number);
        }

        CheckCount(model.Classes.Count, MermaidEngine.MaxElements, source.HeaderLine, "classes");
        return model;
    }

    private static ClassNode Declare(ClassModel model, string raw, Stack<string> namespaces)
    {
        var generic = raw.IndexOf('~');
        var node = model.Ensure(Clean(generic > 0 ? raw[..generic] : raw));
        if (generic > 0 && raw.Length > generic + 1)
        {
            node.Generic ??= Generic(raw[(generic + 1)..^1]);
        }

        if (namespaces.Count > 0)
        {
            node.Namespace ??= namespaces.Peek();
        }

        return node;
    }

    private static string Clean(string id) => id.Trim().Trim('`');

    private static Marker MarkerFor(string token) => token switch
    {
        "<|" or "|>" => Marker.Triangle,
        "*" => Marker.FilledDiamond,
        "o" => Marker.HollowDiamond,
        "<" or ">" => Marker.OpenArrow,
        "()" => Marker.Lollipop,
        _ => Marker.None
    };

    private static void AddMemberOrAnnotation(ClassNode node, string raw)
    {
        var text = raw.Trim();
        if (text.Length == 0)
        {
            return;
        }

        if (text.StartsWith("<<", StringComparison.Ordinal) && text.EndsWith(">>", StringComparison.Ordinal))
        {
            node.Annotations.Add(text[2..^2].Trim());
            return;
        }

        CheckCount(node.Attributes.Count + node.Methods.Count + 1, MermaidEngine.MaxElements, 0, "members in one class");
        var isStatic = false;
        var isAbstract = false;
        if (text.EndsWith('$'))
        {
            isStatic = true;
            text = text[..^1].TrimEnd();
        }
        else if (text.EndsWith('*'))
        {
            isAbstract = true;
            text = text[..^1].TrimEnd();
        }

        var isMethod = text.Contains('(', StringComparison.Ordinal) && text.Contains(')', StringComparison.Ordinal);
        if (isMethod)
        {
            var close = text.LastIndexOf(')');
            var tail = text[(close + 1)..].Trim();
            if (tail.StartsWith('$') || tail.StartsWith('*'))
            {
                isStatic |= tail[0] == '$';
                isAbstract |= tail[0] == '*';
                tail = tail[1..].TrimStart();
            }

            if (tail.EndsWith('$'))
            {
                isStatic = true;
                tail = tail[..^1].TrimEnd();
            }
            else if (tail.EndsWith('*'))
            {
                isAbstract = true;
                tail = tail[..^1].TrimEnd();
            }

            text = text[..(close + 1)] + (tail.Length > 0 ? " " + tail : "");
        }

        var member = new ClassMember(Generic(text), isMethod, isStatic, isAbstract);
        (isMethod ? node.Methods : node.Attributes).Add(member);
    }

    /// <summary>Turns Mermaid's <c>~T~</c> generic notation into angle brackets.</summary>
    public static string Generic(string text)
    {
        if (!text.Contains('~', StringComparison.Ordinal))
        {
            return text;
        }

        var result = new StringBuilder(text.Length);
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '~')
            {
                result.Append(c);
                continue;
            }

            var opens = i + 1 < text.Length && (IsIdChar(text[i + 1]) || text[i + 1] is '~' or '(' or '[') && !(depth > 0 && text[i + 1] == '~');
            if (opens || depth == 0)
            {
                result.Append('<');
                depth++;
            }
            else
            {
                result.Append('>');
                depth--;
            }
        }

        return result.ToString();
    }
}
