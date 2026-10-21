using System.Globalization;
using System.Text;

namespace MkMermaid.Text;

/// <summary>Text metrics in pixels: advance width plus ascent and descent from the baseline.</summary>
public readonly record struct TextMetrics(double Width, double Ascent, double Descent)
{
    public double Height => Ascent + Descent;
}

/// <summary>
/// Measures single lines of text. Hosts supply an implementation backed by their real font stack so labels fit
/// exactly; it must be safe to call from the thread that runs the layout.
/// </summary>
public interface ITextMeasurer
{
    /// <summary>CSS font-family list written to SVG output; it should name the font the measurer models.</summary>
    string FontFamily { get; }

    TextMetrics Measure(string text, double fontSize, bool bold, bool italic);
}

/// <summary>Deterministic measurer based on Helvetica/Arial advance widths; no platform font access.</summary>
public sealed class DefaultTextMeasurer : ITextMeasurer
{
    public static DefaultTextMeasurer Instance { get; } = new();

    // Advance widths per 1000 units for U+0020..U+007E.
    private static readonly short[] Ascii =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584
    ];

    public string FontFamily => "Arial, Helvetica, sans-serif";

    public TextMetrics Measure(string text, double fontSize, bool bold, bool italic)
    {
        var units = 0.0;
        foreach (var rune in text.EnumerateRunes())
        {
            units += Advance(rune);
        }

        var width = units / 1000 * fontSize * (bold ? 1.07 : 1);
        return new TextMetrics(width, fontSize * 0.905, fontSize * 0.212);
    }

    private static double Advance(Rune rune)
    {
        var value = rune.Value;
        if (value is >= 0x20 and <= 0x7E)
        {
            return Ascii[value - 0x20];
        }

        if (value is 0x09)
        {
            return 1112;
        }

        if (value is >= 0x0300 and <= 0x036F or 0x200B or 0x200C or 0x200D or 0xFE0F)
        {
            return 0;
        }

        if (IsWide(value))
        {
            return 1000;
        }

        // Accented Latin letters take the width of their base letter.
        var decomposed = rune.ToString().Normalize(NormalizationForm.FormD);
        if (decomposed.Length > 1 && decomposed[0] is >= ' ' and <= '~')
        {
            return Ascii[decomposed[0] - 0x20];
        }

        return Rune.GetUnicodeCategory(rune) switch
        {
            UnicodeCategory.UppercaseLetter => 700,
            UnicodeCategory.SpaceSeparator => 278,
            UnicodeCategory.OtherSymbol => 1000,
            _ => 556
        };
    }

    private static bool IsWide(int value) =>
        value is >= 0x1100 and <= 0x115F
            or >= 0x2E80 and <= 0xA4CF
            or >= 0xAC00 and <= 0xD7A3
            or >= 0xF900 and <= 0xFAFF
            or >= 0xFE30 and <= 0xFE4F
            or >= 0xFF00 and <= 0xFF60
            or >= 0xFFE0 and <= 0xFFE6
            or >= 0x1F300 and <= 0x1FAFF
            or >= 0x20000 and <= 0x3FFFD;
}
