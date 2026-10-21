using System.Globalization;
using MkMermaid.Theming;

namespace MkMermaid.Rendering;

/// <summary>The subset of CSS that Mermaid's classDef, style and linkStyle statements use.</summary>
internal sealed record Style(
    string? Fill = null,
    string? Stroke = null,
    double? StrokeWidth = null,
    IReadOnlyList<double>? Dash = null,
    string? Color = null,
    bool? Bold = null,
    bool? Italic = null,
    double? FontSize = null,
    double? Opacity = null,
    double? Radius = null)
{
    public static readonly Style None = new();

    public static Style Parse(string? css)
    {
        if (string.IsNullOrWhiteSpace(css))
        {
            return None;
        }

        var style = None;
        foreach (var declaration in SplitDeclarations(css))
        {
            var colon = declaration.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var name = declaration[..colon].Trim().ToLowerInvariant();
            var value = declaration[(colon + 1)..].Trim();
            if (value.EndsWith("!important", StringComparison.OrdinalIgnoreCase))
            {
                value = value[..^10].Trim();
            }

            style = name switch
            {
                "fill" or "background" or "background-color" => style with { Fill = ColorValue(value) },
                "stroke" or "border-color" => style with { Stroke = ColorValue(value) },
                "stroke-width" or "border-width" => Length(value) is { } w ? style with { StrokeWidth = Math.Clamp(w, 0, 20) } : style,
                "stroke-dasharray" => style with { Dash = DashValue(value) },
                "color" => style with { Color = ColorValue(value) },
                "font-weight" => style with { Bold = value is "bold" or "bolder" || (int.TryParse(value, out var weight) && weight >= 600) },
                "font-style" => style with { Italic = value is "italic" or "oblique" },
                "font-size" => Length(value) is { } size ? style with { FontSize = Math.Clamp(size, 6, 72) } : style,
                "opacity" or "fill-opacity" => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var o) ? style with { Opacity = Math.Clamp(o, 0, 1) } : style,
                "rx" or "ry" or "border-radius" => Length(value) is { } r ? style with { Radius = Math.Clamp(r, 0, 100) } : style,
                _ => style
            };
        }

        return style;
    }

    /// <summary>Later values win; null properties keep the earlier ones.</summary>
    public Style Merge(Style? other) => other is null ? this : new Style(
        other.Fill ?? Fill,
        other.Stroke ?? Stroke,
        other.StrokeWidth ?? StrokeWidth,
        other.Dash ?? Dash,
        other.Color ?? Color,
        other.Bold ?? Bold,
        other.Italic ?? Italic,
        other.FontSize ?? FontSize,
        other.Opacity ?? Opacity,
        other.Radius ?? Radius);

    private static IEnumerable<string> SplitDeclarations(string css)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < css.Length; i++)
        {
            switch (css[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth = Math.Max(0, depth - 1);
                    break;
                case ',' or ';' when depth == 0:
                    yield return css[start..i];
                    start = i + 1;
                    break;
            }
        }

        yield return css[start..];
    }

    private static string? ColorValue(string value) =>
        value.Equals("none", StringComparison.OrdinalIgnoreCase) ? "none" : Theming.Color.TryParse(value, out var c) ? c.ToString() : null;

    private static double? Length(string value)
    {
        var text = value.Trim();
        foreach (var unit in new[] { "px", "pt", "em" })
        {
            if (text.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
            {
                var factor = unit switch { "pt" => 4.0 / 3, "em" => 16, _ => 1 };
                return double.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n * factor : null;
            }
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var plain) && double.IsFinite(plain) ? plain : null;
    }

    private static List<double>? DashValue(string value)
    {
        var parts = value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        var result = new List<double>();
        foreach (var part in parts)
        {
            if (Length(part) is { } n && n >= 0)
            {
                result.Add(Math.Min(n, 100));
            }
        }

        return result.Count == 0 || result.All(d => d == 0) ? null : result;
    }

    /// <summary>Resolves a style color: "none" becomes null (unpainted), unset keeps the fallback.</summary>
    public static string? Resolve(string? value, string? fallback) => value switch
    {
        null => fallback,
        "none" => null,
        _ => value
    };
}
