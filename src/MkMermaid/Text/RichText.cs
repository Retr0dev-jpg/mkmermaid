using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MkMermaid.Scene;

namespace MkMermaid.Text;

/// <summary>Label text split into lines of styled runs.</summary>
internal sealed partial class RichText
{
    public static readonly RichText Empty = new([], false);

    private RichText(IReadOnlyList<IReadOnlyList<TextRun>> lines, bool wrap)
    {
        Lines = lines;
        Wrap = wrap;
    }

    public IReadOnlyList<IReadOnlyList<TextRun>> Lines { get; }

    /// <summary>Markdown strings wrap automatically, like Mermaid's markdown labels.</summary>
    public bool Wrap { get; }

    public bool IsEmpty => Lines.All(l => l.All(r => r.Text.Length == 0));

    public string PlainText => string.Join("\n", Lines.Select(l => string.Concat(l.Select(r => r.Text))));

    public RichText Bold() => new(Lines.Select(l => (IReadOnlyList<TextRun>)l.Select(r => r with { Bold = true }).ToList()).ToList(), Wrap);

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BreakTag();

    [GeneratedRegex(@"#([A-Za-z][A-Za-z0-9]{1,31}|[0-9]{1,7});", RegexOptions.CultureInvariant)]
    private static partial Regex EntityCode();

    [GeneratedRegex(@"\bfa[bklrs]?:fa-[\w-]+\s?", RegexOptions.CultureInvariant)]
    private static partial Regex IconToken();

    [GeneratedRegex(@"<(/?)([A-Za-z][A-Za-z0-9]*)\b[^<>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTag();

    /// <summary>Parses a label as written in a diagram (outer quotes already removed).</summary>
    public static RichText Parse(string? raw, bool literal = false)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return Empty;
        }

        if (literal)
        {
            return new RichText(raw.Split('\n').Select(l => (IReadOnlyList<TextRun>)[new TextRun(l)]).ToList(), false);
        }

        var text = raw;
        var markdown = text.Length >= 2 && text[0] == '`' && text[^1] == '`';
        if (markdown)
        {
            text = text[1..^1];
        }

        text = DecodeEntities(text);
        text = IconToken().Replace(text, "");
        text = BreakTag().Replace(text, "\n");
        text = text.Replace("\r", "", StringComparison.Ordinal);
        if (markdown)
        {
            text = string.Join("\n", text.Split('\n').Select(l => l.Trim()));
        }

        var lines = new List<IReadOnlyList<TextRun>>();
        foreach (var line in text.Split('\n'))
        {
            lines.Add(markdown ? ParseMarkdown(line) : ParseHtml(line));
        }

        return new RichText(lines, markdown);
    }

    public static string DecodeEntities(string text)
    {
        if (!text.Contains('#') && !text.Contains('&'))
        {
            return text;
        }

        var replaced = EntityCode().Replace(text, m => char.IsAsciiDigit(m.Groups[1].Value[0]) ? $"&#{m.Groups[1].Value};" : $"&{m.Groups[1].Value};");
        return WebUtility.HtmlDecode(replaced);
    }

    /// <summary>Strips formatting and returns the plain text of a label, for ids, titles and tooltips.</summary>
    public static string Plain(string? raw) => Parse(raw).PlainText;

    private static List<TextRun> ParseHtml(string line)
    {
        var runs = new List<TextRun>();
        int bold = 0, italic = 0, underline = 0, strike = 0;
        var index = 0;
        foreach (Match tag in HtmlTag().Matches(line))
        {
            Add(runs, line[index..tag.Index], bold > 0, italic > 0, underline > 0, strike > 0);
            index = tag.Index + tag.Length;
            var delta = tag.Groups[1].Value == "/" ? -1 : 1;
            switch (tag.Groups[2].Value.ToLowerInvariant())
            {
                case "b" or "strong":
                    bold = Math.Max(0, bold + delta);
                    break;
                case "i" or "em":
                    italic = Math.Max(0, italic + delta);
                    break;
                case "u" or "ins":
                    underline = Math.Max(0, underline + delta);
                    break;
                case "s" or "del" or "strike":
                    strike = Math.Max(0, strike + delta);
                    break;
            }
        }

        Add(runs, line[index..], bold > 0, italic > 0, underline > 0, strike > 0);
        if (runs.Count == 0)
        {
            runs.Add(new TextRun(""));
        }

        return runs;
    }

    private static List<TextRun> ParseMarkdown(string line)
    {
        var runs = new List<TextRun>();
        var builder = new StringBuilder();
        bool bold = false, italic = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '\\' && i + 1 < line.Length && "*_`\\".Contains(line[i + 1]))
            {
                builder.Append(line[++i]);
                continue;
            }

            if ((c == '*' || c == '_') && i + 1 < line.Length && line[i + 1] == c)
            {
                Flush();
                bold = !bold;
                i++;
                continue;
            }

            if (c == '*' || (c == '_' && (i == 0 || !char.IsLetterOrDigit(line[i - 1]) || italic)))
            {
                Flush();
                italic = !italic;
                continue;
            }

            builder.Append(c);
        }

        Flush();
        if (runs.Count == 0)
        {
            runs.Add(new TextRun(""));
        }

        return runs;

        void Flush()
        {
            Add(runs, builder.ToString(), bold, italic, false, false);
            builder.Clear();
        }
    }

    private static void Add(List<TextRun> runs, string text, bool bold, bool italic, bool underline, bool strike)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (runs.Count > 0 && runs[^1] is var last && last.Bold == bold && last.Italic == italic && last.Underline == underline && last.Strike == strike)
        {
            runs[^1] = last with { Text = last.Text + text };
            return;
        }

        runs.Add(new TextRun(text, bold, italic, underline, strike));
    }

    public static RichText FromLines(IEnumerable<IReadOnlyList<TextRun>> lines) => new(lines.ToList(), false);

    public static RichText Styled(string text, bool bold = false, bool italic = false) =>
        new(text.Split('\n').Select(l => (IReadOnlyList<TextRun>)[new TextRun(l, bold, italic)]).ToList(), false);
}

/// <summary>A label measured and wrapped for a given font size.</summary>
internal sealed record MeasuredText(
    IReadOnlyList<(IReadOnlyList<TextRun> Runs, double Width)> Lines,
    double Width,
    double Height,
    double LineHeight,
    double Ascent,
    double Descent,
    double FontSize)
{
    public static MeasuredText Measure(RichText text, double fontSize, ITextMeasurer measurer, double maxWidth = double.PositiveInfinity, bool forceWrap = false)
    {
        var metrics = measurer.Measure("Hg", fontSize, false, false);
        var lineHeight = Math.Max(fontSize * 1.5, metrics.Height);
        var lines = new List<(IReadOnlyList<TextRun>, double)>();
        var wrap = (text.Wrap || forceWrap) && double.IsFinite(maxWidth);
        foreach (var line in text.Lines)
        {
            var width = RunsWidth(line, fontSize, measurer);
            if (!wrap || width <= maxWidth)
            {
                lines.Add((line, width));
                continue;
            }

            foreach (var wrapped in WrapLine(line, fontSize, measurer, maxWidth))
            {
                lines.Add((wrapped, RunsWidth(wrapped, fontSize, measurer)));
            }
        }

        if (text.IsEmpty)
        {
            return new MeasuredText([], 0, 0, lineHeight, metrics.Ascent, metrics.Descent, fontSize);
        }

        return new MeasuredText(lines, lines.Count == 0 ? 0 : lines.Max(l => l.Item2), lines.Count * lineHeight, lineHeight, metrics.Ascent, metrics.Descent, fontSize);
    }

    public static double RunsWidth(IReadOnlyList<TextRun> runs, double fontSize, ITextMeasurer measurer)
    {
        var total = 0.0;
        foreach (var run in runs)
        {
            total += measurer.Measure(run.Text, fontSize, run.Bold, run.Italic).Width;
        }

        return total;
    }

    private static IEnumerable<IReadOnlyList<TextRun>> WrapLine(IReadOnlyList<TextRun> line, double fontSize, ITextMeasurer measurer, double maxWidth)
    {
        var words = new List<TextRun>();
        foreach (var run in line)
        {
            var parts = run.Text.Split(' ');
            for (var i = 0; i < parts.Length; i++)
            {
                words.Add(run with { Text = i < parts.Length - 1 ? parts[i] + " " : parts[i] });
            }
        }

        var current = new List<TextRun>();
        var width = 0.0;
        foreach (var word in words.Where(w => w.Text.Length > 0))
        {
            var w = measurer.Measure(word.Text.TrimEnd(), fontSize, word.Bold, word.Italic).Width;
            if (current.Count > 0 && width + w > maxWidth)
            {
                yield return Trim(current);
                current = [];
                width = 0;
            }

            current.Add(word);
            width += measurer.Measure(word.Text, fontSize, word.Bold, word.Italic).Width;
        }

        if (current.Count > 0)
        {
            yield return Trim(current);
        }
    }

    private static List<TextRun> Trim(List<TextRun> runs)
    {
        if (runs.Count > 0)
        {
            runs[^1] = runs[^1] with { Text = runs[^1].Text.TrimEnd() };
        }

        return runs;
    }
}
