using System.Globalization;
using System.Text;

namespace MkMermaid.Config;

/// <summary>
/// Lenient JSON5 reader for <c>%%{init: ...}%%</c> directives: single or double quotes, unquoted keys,
/// trailing commas. Produces Dictionary/List/string/double/bool/null trees.
/// </summary>
internal sealed class Json5Reader
{
    private const int MaxDepth = 32;
    private readonly string _text;
    private int _pos;

    private Json5Reader(string text) => _text = text;

    public static object? Parse(string text)
    {
        var reader = new Json5Reader(text);
        var value = reader.ReadValue(0);
        reader.SkipSpace();
        return reader._pos == reader._text.Length ? value : throw new FormatException("Unexpected text after the value.");
    }

    private object? ReadValue(int depth)
    {
        if (depth > MaxDepth)
        {
            throw new FormatException("Nesting is too deep.");
        }

        SkipSpace();
        if (_pos >= _text.Length)
        {
            throw new FormatException("Unexpected end.");
        }

        var c = _text[_pos];
        switch (c)
        {
            case '{':
                return ReadObject(depth);
            case '[':
                return ReadArray(depth);
            case '"' or '\'':
                return ReadString();
        }

        var start = _pos;
        while (_pos < _text.Length && !",}]:".Contains(_text[_pos]))
        {
            _pos++;
        }

        var word = _text[start.._pos].Trim();
        return word switch
        {
            "true" => true,
            "false" => false,
            "null" => null,
            _ when double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) => n,
            _ => word
        };
    }

    private Dictionary<string, object?> ReadObject(int depth)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        _pos++;
        while (true)
        {
            SkipSpace();
            if (_pos >= _text.Length)
            {
                throw new FormatException("Unclosed object.");
            }

            if (_text[_pos] == '}')
            {
                _pos++;
                return result;
            }

            string key;
            if (_text[_pos] is '"' or '\'')
            {
                key = ReadString();
            }
            else
            {
                var start = _pos;
                while (_pos < _text.Length && _text[_pos] != ':' && !char.IsWhiteSpace(_text[_pos]))
                {
                    _pos++;
                }

                key = _text[start.._pos];
            }

            SkipSpace();
            if (_pos >= _text.Length || _text[_pos] != ':')
            {
                throw new FormatException("Expected ':'.");
            }

            _pos++;
            result[key] = ReadValue(depth + 1);
            SkipSpace();
            if (_pos < _text.Length && _text[_pos] == ',')
            {
                _pos++;
            }
        }
    }

    private List<object?> ReadArray(int depth)
    {
        var result = new List<object?>();
        _pos++;
        while (true)
        {
            SkipSpace();
            if (_pos >= _text.Length)
            {
                throw new FormatException("Unclosed array.");
            }

            if (_text[_pos] == ']')
            {
                _pos++;
                return result;
            }

            result.Add(ReadValue(depth + 1));
            SkipSpace();
            if (_pos < _text.Length && _text[_pos] == ',')
            {
                _pos++;
            }
        }
    }

    private string ReadString()
    {
        var quote = _text[_pos++];
        var builder = new StringBuilder();
        while (_pos < _text.Length && _text[_pos] != quote)
        {
            var c = _text[_pos++];
            if (c == '\\' && _pos < _text.Length)
            {
                var e = _text[_pos++];
                builder.Append(e switch { 'n' => '\n', 't' => '\t', 'r' => '\r', _ => e });
            }
            else
            {
                builder.Append(c);
            }
        }

        if (_pos >= _text.Length)
        {
            throw new FormatException("Unclosed string.");
        }

        _pos++;
        return builder.ToString();
    }

    private void SkipSpace()
    {
        while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
        {
            _pos++;
        }
    }
}

/// <summary>YAML subset used by Mermaid front matter: nested maps by indentation, scalars, inline lists.</summary>
internal static class YamlReader
{
    public static Dictionary<string, object?> Parse(IReadOnlyList<string> lines)
    {
        var root = new Dictionary<string, object?>(StringComparer.Ordinal);
        var stack = new List<(int Indent, Dictionary<string, object?> Map)> { (-1, root) };
        string? pendingKey = null;
        var pendingIndent = 0;
        foreach (var raw in lines)
        {
            var line = StripComment(raw);
            if (line.Trim().Length == 0)
            {
                continue;
            }

            var indent = line.Length - line.TrimStart().Length;
            var content = line.Trim();
            if (pendingKey is not null)
            {
                var parent = stack[^1].Map;
                if (indent > pendingIndent)
                {
                    var child = new Dictionary<string, object?>(StringComparer.Ordinal);
                    parent[pendingKey] = child;
                    stack.Add((pendingIndent, child));
                }
                else
                {
                    parent[pendingKey] = null;
                }

                pendingKey = null;
            }

            while (stack.Count > 1 && indent <= stack[^1].Indent)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            if (content.StartsWith("- ", StringComparison.Ordinal))
            {
                continue;
            }

            var colon = FindColon(content);
            if (colon <= 0)
            {
                continue;
            }

            var key = Unquote(content[..colon].Trim());
            var value = content[(colon + 1)..].Trim();
            if (value.Length == 0)
            {
                pendingKey = key;
                pendingIndent = indent;
                continue;
            }

            stack[^1].Map[key] = Scalar(value);
        }

        if (pendingKey is not null)
        {
            stack[^1].Map[pendingKey] = null;
        }

        return root;
    }

    private static object? Scalar(string value)
    {
        if (value.StartsWith('[') || value.StartsWith('{'))
        {
            try
            {
                return Json5Reader.Parse(value);
            }
            catch (FormatException)
            {
                return value;
            }
        }

        if (value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\''))
        {
            return Unquote(value);
        }

        return value switch
        {
            "true" or "True" => true,
            "false" or "False" => false,
            "null" or "~" => null,
            _ when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) => n,
            _ => value
        };
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\''))
        {
            var inner = value[1..^1];
            return value[0] == '"' ? inner.Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\n", "\n", StringComparison.Ordinal) : inner.Replace("''", "'", StringComparison.Ordinal);
        }

        return value;
    }

    private static int FindColon(string content)
    {
        var quote = '\0';
        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
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
            else if (c == ':' && (i + 1 == content.Length || content[i + 1] == ' '))
            {
                return i;
            }
        }

        return -1;
    }

    private static string StripComment(string line)
    {
        var quote = '\0';
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
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
            else if (c == '#' && (i == 0 || line[i - 1] == ' '))
            {
                return line[..i];
            }
        }

        return line;
    }
}
