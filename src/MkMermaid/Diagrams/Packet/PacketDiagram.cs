using System.Text.RegularExpressions;
using MkMermaid.Parsing;
using MkMermaid.Rendering;
using MkMermaid.Scene;
using static MkMermaid.Parsing.ParseHelpers;

namespace MkMermaid.Diagrams.Packet;

internal sealed record PacketBlock(int Start, int End, string Label);

internal sealed class PacketModel : IDiagramModel
{
    public string? Title { get; set; }

    public List<PacketBlock> Blocks { get; } = [];

    public DiagramScene Render(RenderContext context, DiagramInfo info) => PacketRenderer.Render(this, context, info);
}

internal static partial class PacketParser
{
    [GeneratedRegex(@"^(?:(?<start>\d+)(?:\s*-\s*(?<end>\d+))?|\+(?<count>\d+))\s*:\s*""(?<label>[^""]*)""\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex BlockLine();

    /// <summary>Upper bound on the bit offset so a typo cannot allocate thousands of rows.</summary>
    private const int MaxBits = 1 << 16;

    public static PacketModel Parse(Source source)
    {
        var model = new PacketModel();
        var next = 0;
        foreach (var line in source.Lines)
        {
            var text = line.Trimmed;
            if (StartsWithWord(text, "title"))
            {
                model.Title = Unquote(text[5..].Trim());
                continue;
            }

            var match = BlockLine().Match(text);
            if (!match.Success)
            {
                throw Error(line, $"Unrecognized statement '{Source.Truncate(text)}'; a field is written start-end: \"Label\".");
            }

            int start, end;
            if (match.Groups["count"].Success)
            {
                if (!int.TryParse(match.Groups["count"].Value, out var count) || count < 1)
                {
                    throw new MermaidParseException(MermaidErrorCode.InvalidValue, "A field needs at least one bit.", line.Number);
                }

                start = next;
                end = next + count - 1;
            }
            else
            {
                if (!int.TryParse(match.Groups["start"].Value, out start))
                {
                    start = int.MaxValue;
                }

                end = start;
                if (match.Groups["end"].Success && !int.TryParse(match.Groups["end"].Value, out end))
                {
                    end = int.MaxValue;
                }
            }

            if (end < start)
            {
                throw new MermaidParseException(MermaidErrorCode.InvalidValue, $"Field \"{Source.Truncate(match.Groups["label"].Value)}\" ends ({end}) before it starts ({start}).", line.Number);
            }

            if (start != next)
            {
                throw new MermaidParseException(MermaidErrorCode.InvalidValue, $"Field \"{Source.Truncate(match.Groups["label"].Value)}\" starts at bit {start}; expected {next}.", line.Number);
            }

            if (end >= MaxBits)
            {
                throw new MermaidParseException(MermaidErrorCode.TooComplex, $"The packet is longer than {MaxBits} bits.", line.Number);
            }

            CheckCount(model.Blocks.Count + 1, MermaidEngine.MaxElements, line.Number, "fields");
            model.Blocks.Add(new PacketBlock(start, end, match.Groups["label"].Value));
            next = end + 1;
        }

        return model;
    }
}

internal static class PacketRenderer
{
    public static DiagramScene Render(PacketModel model, RenderContext context, DiagramInfo info)
    {
        var theme = context.Theme;
        var config = context.Config;
        var rowHeight = config.Number("packet", "rowHeight", 32);
        var bitWidth = config.Number("packet", "bitWidth", 32);
        var bitsPerRow = (int)Math.Clamp(config.Number("packet", "bitsPerRow", 32), 1, 256);
        var showBits = config.Flag("packet", "showBits", true);
        var paddingX = config.Number("packet", "paddingX", 5);
        var paddingY = config.Number("packet", "paddingY", 5) + (showBits ? 10 : 0);
        var fill = theme.Hex("packet.blockFillColor", "#EFEFEF");
        var stroke = theme.Hex("packet.blockStrokeColor", "#000000");
        var strokeWidth = theme.Number("packet.blockStrokeWidth", 1);
        var labelColor = theme.Hex("packet.labelColor", "#000000");
        var labelSize = theme.Number("packet.labelFontSize", 12);
        var byteSize = theme.Number("packet.byteFontSize", 10);
        var startColor = theme.Hex("packet.startByteColor", theme.Hex("textColor"));
        var endColor = theme.Hex("packet.endByteColor", theme.Hex("textColor"));
        var canvas = context.CreateCanvas();

        // Fields that cross a row boundary are split into one box per row.
        foreach (var block in model.Blocks)
        {
            var start = block.Start;
            while (start <= block.End)
            {
                var row = start / bitsPerRow;
                var end = Math.Min(block.End, (row + 1) * bitsPerRow - 1);
                var x = start % bitsPerRow * bitWidth + 1;
                var y = row * (rowHeight + paddingY) + paddingY;
                var w = (end - start + 1) * bitWidth - paddingX;
                canvas.Rect(x, y, w, rowHeight, fill, stroke, strokeWidth);
                var label = context.Measure(block.Label, labelSize, Math.Max(w - 4, 8));
                canvas.Label(label, x + w / 2, y + (rowHeight - label.Height) / 2, TextAnchor.Middle, labelColor);
                if (showBits)
                {
                    var single = start == end;
                    canvas.Text(start.ToString(System.Globalization.CultureInfo.InvariantCulture), single ? x + w / 2 : x, y - 2, byteSize,
                        single ? TextAnchor.Middle : TextAnchor.Start, startColor);
                    if (!single)
                    {
                        canvas.Text(end.ToString(System.Globalization.CultureInfo.InvariantCulture), x + w, y - 2, byteSize, TextAnchor.End, endColor);
                    }
                }

                start = end + 1;
            }
        }

        if (!string.IsNullOrWhiteSpace(info.Title))
        {
            var rows = model.Blocks.Count == 0 ? 0 : model.Blocks[^1].End / bitsPerRow + 1;
            var size = theme.Number("packet.titleFontSize", 14);
            canvas.Text(info.Title, bitsPerRow * bitWidth / 2, rows * (rowHeight + paddingY) + paddingY + size, size, TextAnchor.Middle,
                theme.Hex("packet.titleColor", theme.Hex("textColor")));
        }

        return canvas.Finish(DiagramKind.Packet, info.Title, info.AccDescription, 5, null);
    }
}
