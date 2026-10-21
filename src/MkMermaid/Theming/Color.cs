using System.Globalization;

namespace MkMermaid.Theming;

/// <summary>An sRGB color with alpha; channels in 0..255, alpha in 0..1.</summary>
internal readonly record struct Color(double R, double G, double B, double A = 1)
{
    private static readonly Dictionary<string, uint> Named = BuildNamed();

    public static Color Black => new(0, 0, 0);

    public static Color White => new(255, 255, 255);

    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim().TrimEnd(';').Trim();
        if (value.EndsWith("!important", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^10].Trim();
        }

        if (value.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            color = new Color(0, 0, 0, 0);
            return true;
        }

        if (Named.TryGetValue(value, out var rgb))
        {
            color = new Color((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            return true;
        }

        if (value.StartsWith('#'))
        {
            return TryParseHex(value[1..], out color);
        }

        var open = value.IndexOf('(');
        if (open > 0 && value.EndsWith(')'))
        {
            var name = value[..open].Trim().ToLowerInvariant();
            var args = value[(open + 1)..^1].Replace("/", ",", StringComparison.Ordinal)
                .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (args.Length is < 3 or > 4)
            {
                return false;
            }

            var alpha = 1.0;
            if (args.Length == 4 && !TryComponent(args[3], 1, out alpha))
            {
                return false;
            }

            if (name is "rgb" or "rgba")
            {
                if (TryComponent(args[0], 255, out var r) && TryComponent(args[1], 255, out var g) && TryComponent(args[2], 255, out var b))
                {
                    color = new Color(Clamp(r, 255), Clamp(g, 255), Clamp(b, 255), Clamp(alpha, 1));
                    return true;
                }
            }
            else if (name is "hsl" or "hsla")
            {
                if (double.TryParse(args[0].TrimEnd('°').Replace("deg", "", StringComparison.OrdinalIgnoreCase), NumberStyles.Float, CultureInfo.InvariantCulture, out var h) &&
                    TryComponent(args[1], 100, out var s) && TryComponent(args[2], 100, out var l))
                {
                    color = FromHsl(h, s, l) with { A = Clamp(alpha, 1) };
                    return true;
                }
            }
        }

        return false;
    }

    public static Color Parse(string text) => TryParse(text, out var color) ? color : Black;

    private static bool TryParseHex(string hex, out Color color)
    {
        color = default;
        if (!hex.All(Uri.IsHexDigit))
        {
            return false;
        }

        if (hex.Length is 3 or 4)
        {
            hex = string.Concat(hex.Select(c => $"{c}{c}"));
        }

        if (hex.Length is not (6 or 8))
        {
            return false;
        }

        var value = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        color = hex.Length == 6
            ? new Color((value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF)
            : new Color((value >> 24) & 0xFF, (value >> 16) & 0xFF, (value >> 8) & 0xFF, (value & 0xFF) / 255.0);
        return true;
    }

    private static bool TryComponent(string text, double full, out double value)
    {
        var percent = text.EndsWith('%');
        var ok = double.TryParse(percent ? text[..^1] : text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        if (ok && percent)
        {
            value = value / 100 * full;
        }

        return ok && double.IsFinite(value);
    }

    private static double Clamp(double value, double max) => Math.Clamp(value, 0, max);

    public override string ToString()
    {
        var r = (int)Math.Round(Clamp(R, 255));
        var g = (int)Math.Round(Clamp(G, 255));
        var b = (int)Math.Round(Clamp(B, 255));
        var a = (int)Math.Round(Clamp(A, 1) * 255);
        return a >= 255
            ? string.Create(CultureInfo.InvariantCulture, $"#{r:X2}{g:X2}{b:X2}")
            : string.Create(CultureInfo.InvariantCulture, $"#{r:X2}{g:X2}{b:X2}{a:X2}");
    }

    public (double H, double S, double L) ToHsl()
    {
        var r = R / 255;
        var g = G / 255;
        var b = B / 255;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max - min < 1e-12)
        {
            return (0, 0, l * 100);
        }

        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if (max == r)
        {
            h = (g - b) / d + (g < b ? 6 : 0);
        }
        else if (max == g)
        {
            h = (b - r) / d + 2;
        }
        else
        {
            h = (r - g) / d + 4;
        }

        return (h * 60, s * 100, l * 100);
    }

    public static Color FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360 / 360;
        s = Math.Clamp(s, 0, 100) / 100;
        l = Math.Clamp(l, 0, 100) / 100;
        if (s == 0)
        {
            return new Color(l * 255, l * 255, l * 255);
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        return new Color(Hue(p, q, h + 1.0 / 3) * 255, Hue(p, q, h) * 255, Hue(p, q, h - 1.0 / 3) * 255);

        static double Hue(double p, double q, double t)
        {
            if (t < 0)
            {
                t += 1;
            }

            if (t > 1)
            {
                t -= 1;
            }

            return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
        }
    }

    /// <summary>Like khroma's adjust: adds the given deltas to HSL (degrees, percentage points) and RGB channels.</summary>
    public Color Adjust(double h = 0, double s = 0, double l = 0, double r = 0, double g = 0, double b = 0)
    {
        var color = this;
        if (r != 0 || g != 0 || b != 0)
        {
            color = new Color(Clamp(R + r, 255), Clamp(G + g, 255), Clamp(B + b, 255), A);
        }

        if (h == 0 && s == 0 && l == 0)
        {
            return color;
        }

        var (ch, cs, cl) = color.ToHsl();
        return FromHsl(ch + h, Math.Clamp(cs + s, 0, 100), Math.Clamp(cl + l, 0, 100)) with { A = A };
    }

    public Color Lighten(double amount) => Adjust(l: amount);

    public Color Darken(double amount) => Adjust(l: -amount);

    public Color Invert() => new(255 - R, 255 - G, 255 - B, A);

    public Color WithAlpha(double alpha) => this with { A = Clamp(alpha, 1) };

    public Color Mix(Color other, double weight)
    {
        var w = Math.Clamp(weight, 0, 1);
        return new Color(R * (1 - w) + other.R * w, G * (1 - w) + other.G * w, B * (1 - w) + other.B * w, A * (1 - w) + other.A * w);
    }

    /// <summary>WCAG relative luminance.</summary>
    public double Luminance
    {
        get
        {
            static double Channel(double c)
            {
                c /= 255;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Channel(R) + 0.7152 * Channel(G) + 0.0722 * Channel(B);
        }
    }

    public bool IsDark => Luminance < 0.5 && (R * 299 + G * 587 + B * 114) / 1000 < 128;

    /// <summary>Black or white, whichever reads better on this color.</summary>
    public Color Contrast => IsDark ? new Color(240, 240, 240) : new Color(51, 51, 51);

    private static Dictionary<string, uint> BuildNamed()
    {
        const string table =
            "aliceblue f0f8ff antiquewhite faebd7 aqua 00ffff aquamarine 7fffd4 azure f0ffff beige f5f5dc bisque ffe4c4 black 000000 " +
            "blanchedalmond ffebcd blue 0000ff blueviolet 8a2be2 brown a52a2a burlywood deb887 cadetblue 5f9ea0 chartreuse 7fff00 " +
            "chocolate d2691e coral ff7f50 cornflowerblue 6495ed cornsilk fff8dc crimson dc143c cyan 00ffff darkblue 00008b " +
            "darkcyan 008b8b darkgoldenrod b8860b darkgray a9a9a9 darkgreen 006400 darkgrey a9a9a9 darkkhaki bdb76b darkmagenta 8b008b " +
            "darkolivegreen 556b2f darkorange ff8c00 darkorchid 9932cc darkred 8b0000 darksalmon e9967a darkseagreen 8fbc8f " +
            "darkslateblue 483d8b darkslategray 2f4f4f darkslategrey 2f4f4f darkturquoise 00ced1 darkviolet 9400d3 deeppink ff1493 " +
            "deepskyblue 00bfff dimgray 696969 dimgrey 696969 dodgerblue 1e90ff firebrick b22222 floralwhite fffaf0 forestgreen 228b22 " +
            "fuchsia ff00ff gainsboro dcdcdc ghostwhite f8f8ff gold ffd700 goldenrod daa520 gray 808080 green 008000 greenyellow adff2f " +
            "grey 808080 honeydew f0fff0 hotpink ff69b4 indianred cd5c5c indigo 4b0082 ivory fffff0 khaki f0e68c lavender e6e6fa " +
            "lavenderblush fff0f5 lawngreen 7cfc00 lemonchiffon fffacd lightblue add8e6 lightcoral f08080 lightcyan e0ffff " +
            "lightgoldenrodyellow fafad2 lightgray d3d3d3 lightgreen 90ee90 lightgrey d3d3d3 lightpink ffb6c1 lightsalmon ffa07a " +
            "lightseagreen 20b2aa lightskyblue 87cefa lightslategray 778899 lightslategrey 778899 lightsteelblue b0c4de lightyellow ffffe0 " +
            "lime 00ff00 limegreen 32cd32 linen faf0e6 magenta ff00ff maroon 800000 mediumaquamarine 66cdaa mediumblue 0000cd " +
            "mediumorchid ba55d3 mediumpurple 9370db mediumseagreen 3cb371 mediumslateblue 7b68ee mediumspringgreen 00fa9a " +
            "mediumturquoise 48d1cc mediumvioletred c71585 midnightblue 191970 mintcream f5fffa mistyrose ffe4e1 moccasin ffe4b5 " +
            "navajowhite ffdead navy 000080 oldlace fdf5e6 olive 808000 olivedrab 6b8e23 orange ffa500 orangered ff4500 orchid da70d6 " +
            "palegoldenrod eee8aa palegreen 98fb98 paleturquoise afeeee palevioletred db7093 papayawhip ffefd5 peachpuff ffdab9 " +
            "peru cd853f pink ffc0cb plum dda0dd powderblue b0e0e6 purple 800080 rebeccapurple 663399 red ff0000 rosybrown bc8f8f " +
            "royalblue 4169e1 saddlebrown 8b4513 salmon fa8072 sandybrown f4a460 seagreen 2e8b57 seashell fff5ee sienna a0522d " +
            "silver c0c0c0 skyblue 87ceeb slateblue 6a5acd slategray 708090 slategrey 708090 snow fffafa springgreen 00ff7f " +
            "steelblue 4682b4 tan d2b48c teal 008080 thistle d8bfd8 tomato ff6347 turquoise 40e0d0 violet ee82ee wheat f5deb3 " +
            "white ffffff whitesmoke f5f5f5 yellow ffff00 yellowgreen 9acd32";
        var parts = table.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            result[parts[i]] = uint.Parse(parts[i + 1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return result;
    }
}
