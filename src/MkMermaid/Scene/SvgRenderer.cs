using System.Globalization;
using System.Net;
using System.Text;

namespace MkMermaid.Scene;

public sealed class SvgOptions
{
    /// <summary>Emit the xmlns attribute (needed for standalone files; optional inside HTML).</summary>
    public bool Standalone { get; init; } = true;

    /// <summary>Overrides the accessible title; defaults to the diagram's title.</summary>
    public string? Title { get; init; }

    /// <summary>CSS font-family for text; defaults to the family of the measurer used for layout.</summary>
    public string? FontFamily { get; init; }

    /// <summary>Prefix for element ids (gradients); give each diagram embedded in one document its own prefix.</summary>
    public string IdPrefix { get; init; } = "mkm";
}

/// <summary>Writes a <see cref="DiagramScene"/> as self-contained SVG (no scripts, styles or external references).</summary>
public static class SvgRenderer
{
    public static string Render(DiagramScene scene, SvgOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        options ??= new SvgOptions();
        var svg = new StringBuilder();
        var title = options.Title ?? scene.Title;
        svg.Append("<svg");
        if (options.Standalone)
        {
            svg.Append(" xmlns=\"http://www.w3.org/2000/svg\"");
        }

        svg.Append(" width=\"").Append(N(scene.Width)).Append("\" height=\"").Append(N(scene.Height))
            .Append("\" viewBox=\"0 0 ").Append(N(scene.Width)).Append(' ').Append(N(scene.Height)).Append("\" role=\"img\"");
        if (scene.Error is not null)
        {
            svg.Append(" aria-invalid=\"true\"");
        }

        svg.Append('>');
        if (!string.IsNullOrEmpty(title))
        {
            svg.Append("<title>").Append(Escape(title)).Append("</title>");
        }

        if (!string.IsNullOrEmpty(scene.Description))
        {
            svg.Append("<desc>").Append(Escape(scene.Description)).Append("</desc>");
        }

        if (scene.Background is { } background)
        {
            svg.Append("<rect width=\"100%\" height=\"100%\"");
            Paint(svg, "fill", background);
            svg.Append("/>");
        }

        var family = Escape(options.FontFamily ?? scene.FontFamily);
        var gradients = 0;
        foreach (var item in scene.Items)
        {
            switch (item)
            {
                case ShapeItem { StrokeGradient: { } gradient } shape:
                    var id = Escape(options.IdPrefix) + "-g" + gradients++.ToString(CultureInfo.InvariantCulture);
                    svg.Append("<defs><linearGradient id=\"").Append(id).Append("\" gradientUnits=\"userSpaceOnUse\" x1=\"").Append(N(gradient.X1))
                        .Append("\" y1=\"").Append(N(gradient.Y1)).Append("\" x2=\"").Append(N(gradient.X2)).Append("\" y2=\"").Append(N(gradient.Y2)).Append("\">");
                    svg.Append("<stop offset=\"0\"");
                    Paint(svg, "stop-color", gradient.From);
                    svg.Append("/><stop offset=\"1\"");
                    Paint(svg, "stop-color", gradient.To);
                    svg.Append("/></linearGradient></defs>");
                    WriteShape(svg, shape, "url(#" + id + ")");
                    break;
                case ShapeItem shape:
                    WriteShape(svg, shape);
                    break;
                case TextItem text:
                    WriteText(svg, text, family);
                    break;
            }
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    private static void WriteShape(StringBuilder svg, ShapeItem shape, string? strokeReference = null)
    {
        svg.Append("<path d=\"").Append(PathData(shape.Geometry)).Append('"');
        if (shape.Fill is null)
        {
            svg.Append(" fill=\"none\"");
        }
        else
        {
            Paint(svg, "fill", shape.Fill);
        }

        if (shape.Stroke is not null && shape.StrokeWidth > 0)
        {
            if (strokeReference is null)
            {
                Paint(svg, "stroke", shape.Stroke);
            }
            else
            {
                svg.Append(" stroke=\"").Append(strokeReference).Append('"');
            }

            svg.Append(" stroke-width=\"").Append(N(shape.StrokeWidth)).Append('"');
            if (shape.Dash is { Count: > 0 } dash)
            {
                svg.Append(" stroke-dasharray=\"").Append(string.Join(' ', dash.Select(N))).Append('"');
            }
        }

        if (shape.Opacity < 1)
        {
            svg.Append(" opacity=\"").Append(N(Math.Max(0, shape.Opacity))).Append('"');
        }

        svg.Append("/>");
    }

    private static void WriteText(StringBuilder svg, TextItem text, string family)
    {
        svg.Append("<text x=\"").Append(N(text.X)).Append("\" y=\"").Append(N(text.Y))
            .Append("\" font-family=\"").Append(family).Append("\" font-size=\"").Append(N(text.FontSize)).Append('"');
        if (text.Anchor != TextAnchor.Start)
        {
            svg.Append(" text-anchor=\"").Append(text.Anchor == TextAnchor.Middle ? "middle" : "end").Append('"');
        }

        Paint(svg, "fill", text.Color);
        if (text.Rotation != 0)
        {
            svg.Append(" transform=\"rotate(").Append(N(text.Rotation)).Append(' ').Append(N(text.X)).Append(' ').Append(N(text.Y)).Append(")\"");
        }

        svg.Append(" xml:space=\"preserve\">");
        foreach (var run in text.Runs)
        {
            var plain = !run.Bold && !run.Italic && !run.Underline && !run.Strike;
            if (plain)
            {
                svg.Append(Escape(run.Text));
                continue;
            }

            svg.Append("<tspan");
            if (run.Bold)
            {
                svg.Append(" font-weight=\"bold\"");
            }

            if (run.Italic)
            {
                svg.Append(" font-style=\"italic\"");
            }

            if (run.Underline || run.Strike)
            {
                svg.Append(" text-decoration=\"").Append(run.Underline ? "underline" : "line-through").Append('"');
            }

            svg.Append('>').Append(Escape(run.Text)).Append("</tspan>");
        }

        svg.Append("</text>");
    }

    /// <summary>SVG path data for a geometry (absolute commands).</summary>
    public static string PathData(PathGeometry geometry)
    {
        var d = new StringBuilder();
        foreach (var c in geometry.Commands)
        {
            switch (c.Kind)
            {
                case PathCommandKind.MoveTo:
                    d.Append('M').Append(N(c.X)).Append(' ').Append(N(c.Y));
                    break;
                case PathCommandKind.LineTo:
                    d.Append('L').Append(N(c.X)).Append(' ').Append(N(c.Y));
                    break;
                case PathCommandKind.CubicTo:
                    d.Append('C').Append(N(c.X1)).Append(' ').Append(N(c.Y1)).Append(' ').Append(N(c.X2)).Append(' ').Append(N(c.Y2))
                        .Append(' ').Append(N(c.X)).Append(' ').Append(N(c.Y));
                    break;
                case PathCommandKind.Close:
                    d.Append('Z');
                    break;
            }
        }

        return d.ToString();
    }

    private static void Paint(StringBuilder svg, string attribute, string color)
    {
        if (color.Length == 9 && color[0] == '#')
        {
            var alpha = int.Parse(color.AsSpan(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            svg.Append(' ').Append(attribute).Append("=\"").Append(color.AsSpan(0, 7)).Append('"');
            svg.Append(' ').Append(attribute == "stop-color" ? "stop" : attribute).Append("-opacity=\"").Append(N(alpha)).Append('"');
            return;
        }

        svg.Append(' ').Append(attribute).Append("=\"").Append(Escape(color)).Append('"');
    }

    private static string N(double value)
    {
        if (!double.IsFinite(value))
        {
            return "0";
        }

        var rounded = Math.Round(value, 2);
        return rounded == 0 ? "0" : rounded.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string Escape(string text) => WebUtility.HtmlEncode(XmlSafe(text));

    /// <summary>Replaces characters XML 1.0 forbids even as references (C0 controls, U+FFFE/FFFF, lone surrogates).</summary>
    private static string XmlSafe(string text)
    {
        StringBuilder? safe = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var pair = char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
            var valid = pair || (c >= 0x20 && !char.IsSurrogate(c) && c is not ('\uFFFE' or '\uFFFF')) || c is '\t' or '\n' or '\r';
            if (!valid)
            {
                safe ??= new StringBuilder(text, 0, i, text.Length);
                safe.Append('\uFFFD');
                continue;
            }

            if (safe is not null)
            {
                safe.Append(c);
                if (pair)
                {
                    safe.Append(text[i + 1]);
                }
            }

            if (pair)
            {
                i++;
            }
        }

        return safe?.ToString() ?? text;
    }
}
