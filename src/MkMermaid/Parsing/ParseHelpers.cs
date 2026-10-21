using System.Globalization;

namespace MkMermaid.Parsing;

internal static class ParseHelpers
{
    public static MermaidParseException Error(SourceLine line, string message) => new(MermaidErrorCode.Syntax, message, line.Number);

    public static MermaidParseException Error(int line, string message) => new(MermaidErrorCode.Syntax, message, line);

    public static void CheckCount(int count, int max, int line, string what)
    {
        if (count > max)
        {
            throw new MermaidParseException(MermaidErrorCode.TooComplex, $"The diagram has more than {max} {what}.", line);
        }
    }

    /// <summary>Removes one pair of matching outer quotes.</summary>
    public static string Unquote(string text)
    {
        var t = text.Trim();
        if (t.Length >= 2 && (t[0] == '"' && t[^1] == '"' || t[0] == '\'' && t[^1] == '\''))
        {
            return t[1..^1];
        }

        return t;
    }

    public static bool TryNumber(string text, out double value)
    {
        var ok = double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
        if (!ok)
        {
            value = 0;
        }

        return ok;
    }

    public static bool StartsWithWord(string text, string word) =>
        text.StartsWith(word, StringComparison.Ordinal) && (text.Length == word.Length || !IsIdChar(text[word.Length]));

    public static bool IsIdChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>Splits a line on separators that are outside quotes and brackets.</summary>
    public static List<string> SplitTopLevel(string text, char separator)
    {
        var result = new List<string>();
        var depth = 0;
        var quote = '\0';
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    quote = c;
                    break;
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}':
                    depth = Math.Max(0, depth - 1);
                    break;
                default:
                    if (c == separator && depth == 0 && !(c == ';' && ClosesEntity(text, i)))
                    {
                        result.Add(text[start..i]);
                        start = i + 1;
                    }

                    break;
            }
        }

        result.Add(text[start..]);
        return result;
    }

    /// <summary>
    /// True when the ';' at <paramref name="index"/> ends an entity code (#59;, #quot;, &amp;amp;), which Mermaid
    /// never treats as a statement separator. A color after a colon (fill:#f9f;) is not an entity.
    /// </summary>
    public static bool ClosesEntity(string text, int index)
    {
        var start = index;
        while (start > 0 && index - start < 32 && char.IsAsciiLetterOrDigit(text[start - 1]))
        {
            start--;
        }

        if (start == index || start == 0)
        {
            return false;
        }

        return text[start - 1] switch
        {
            '#' => start < 2 || text[start - 2] != ':',
            '&' => char.IsAsciiLetter(text[start]),
            _ => false
        };
    }

    /// <summary>Parses the small inline map used by <c>@{ key: value, ... }</c> and kanban metadata.</summary>
    public static Dictionary<string, string> InlineMap(string body)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in SplitTopLevel(body.Replace('\n', ','), ','))
        {
            var colon = part.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            map[part[..colon].Trim()] = Unquote(part[(colon + 1)..].Trim());
        }

        return map;
    }
}
