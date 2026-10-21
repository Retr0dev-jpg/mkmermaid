using System.Globalization;

namespace MkMermaid.Config;

/// <summary>Merged diagram configuration from front matter and <c>%%{init}%%</c> directives.</summary>
public sealed class MermaidConfig
{
    private readonly Dictionary<string, object?> _root = new(StringComparer.Ordinal);

    public static MermaidConfig Empty { get; } = new();

    internal void Merge(Dictionary<string, object?> source) => Merge(_root, source, 0);

    private static void Merge(Dictionary<string, object?> target, Dictionary<string, object?> source, int depth)
    {
        if (depth > 16)
        {
            return;
        }

        foreach (var (key, value) in source)
        {
            // Prototype-style keys have no meaning here; ignore them like Mermaid's sanitizer does.
            if (key is "__proto__" or "constructor" or "prototype")
            {
                continue;
            }

            if (value is Dictionary<string, object?> child && target.TryGetValue(key, out var existing) && existing is Dictionary<string, object?> current)
            {
                Merge(current, child, depth + 1);
            }
            else if (value is Dictionary<string, object?> fresh)
            {
                var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
                Merge(copy, fresh, depth + 1);
                target[key] = copy;
            }
            else
            {
                target[key] = value;
            }
        }
    }

    /// <summary>Theme name requested by the diagram, if any.</summary>
    public string? Theme => GetString("theme");

    public object? Get(string path)
    {
        object? current = _root;
        foreach (var part in path.Split('.'))
        {
            if (current is not Dictionary<string, object?> map || !TryGet(map, part, out current))
            {
                return null;
            }
        }

        return current;
    }

    private static bool TryGet(Dictionary<string, object?> map, string key, out object? value)
    {
        if (map.TryGetValue(key, out value))
        {
            return true;
        }

        foreach (var (k, v) in map)
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
            {
                value = v;
                return true;
            }
        }

        return false;
    }

    public string? GetString(string path) => Get(path) switch
    {
        null => null,
        string s => s,
        double d => d.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        _ => null
    };

    /// <summary>Largest magnitude accepted from configuration or theme numbers; anything beyond falls back to the default.</summary>
    public const double MaxNumber = 100_000;

    public double GetNumber(string path, double fallback)
    {
        switch (Get(path))
        {
            case double d when InRange(d):
                return d;
            case string s:
                var text = s.Trim();
                if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase))
                {
                    text = text[..^2];
                }

                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && InRange(n) ? n : fallback;
            default:
                return fallback;
        }
    }

    internal static bool InRange(double value) => double.IsFinite(value) && Math.Abs(value) <= MaxNumber;

    public bool GetBool(string path, bool fallback) => Get(path) switch
    {
        bool b => b,
        string s when bool.TryParse(s, out var b) => b,
        double d => d != 0,
        _ => fallback
    };

    /// <summary>Reads a number from a diagram section, falling back to the top level, then to <paramref name="fallback"/>.</summary>
    internal double Number(string section, string key, double fallback) => GetNumber(section + "." + key, GetNumber(key, fallback));

    internal bool Flag(string section, string key, bool fallback) => GetBool(section + "." + key, GetBool(key, fallback));

    internal string? Text(string section, string key) => GetString(section + "." + key) ?? GetString(key);

    /// <summary>themeVariables flattened to dotted keys with string values.</summary>
    internal Dictionary<string, string> ThemeVariables()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Get("themeVariables") is Dictionary<string, object?> map)
        {
            Flatten(map, "", result, 0);
        }

        return result;
    }

    private static void Flatten(Dictionary<string, object?> map, string prefix, Dictionary<string, string> result, int depth)
    {
        if (depth > 4)
        {
            return;
        }

        foreach (var (key, value) in map)
        {
            var name = prefix + key;
            switch (value)
            {
                case Dictionary<string, object?> child:
                    Flatten(child, name + ".", result, depth + 1);
                    break;
                case string s:
                    result[name] = s;
                    break;
                case double d:
                    result[name] = d.ToString(CultureInfo.InvariantCulture);
                    break;
                case bool b:
                    result[name] = b ? "true" : "false";
                    break;
                case List<object?> list:
                    result[name] = string.Join(",", list.Select(i => Convert.ToString(i, CultureInfo.InvariantCulture)));
                    break;
            }
        }
    }
}
