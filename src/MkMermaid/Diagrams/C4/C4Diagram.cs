using System.Text.RegularExpressions;
using MkMermaid.Layout;
using MkMermaid.Parsing;
using MkMermaid.Rendering;
using MkMermaid.Scene;
using MkMermaid.Text;
using static MkMermaid.Parsing.ParseHelpers;

namespace MkMermaid.Diagrams.C4;

internal enum C4ShapeForm
{
    Box,
    Database,
    Queue
}

internal sealed class C4Element(string alias, string type, C4ShapeForm form, string label)
{
    public string Alias { get; } = alias;

    /// <summary>Mermaid's type name, for example person, external_system or container_db.</summary>
    public string Type { get; } = type;

    public C4ShapeForm Form { get; } = form;

    public string Label { get; } = label;

    public string? Technology { get; set; }

    public string? Description { get; set; }

    public string? Background { get; set; }

    public string? FontColor { get; set; }

    public string? Border { get; set; }

    public Rect Bounds { get; set; }
}

internal sealed class C4Boundary(string alias, string label, string type, bool isNode)
{
    public string Alias { get; } = alias;

    public string Label { get; } = label;

    public string Type { get; } = type;

    /// <summary>Deployment nodes are drawn solid; other boundaries are dashed.</summary>
    public bool IsNode { get; } = isNode;

    public string? Description { get; set; }

    public string? Background { get; set; }

    public string? FontColor { get; set; }

    public string? Border { get; set; }

    public List<C4Element> Elements { get; } = [];

    public List<C4Boundary> Children { get; } = [];

    public Rect Bounds { get; set; }
}

internal sealed class C4Relation(string from, string to, string label, bool both)
{
    public string From { get; } = from;

    public string To { get; } = to;

    public string Label { get; } = label;

    public bool Both { get; } = both;

    public string? Technology { get; set; }

    public string? TextColor { get; set; }

    public string? LineColor { get; set; }

    public double OffsetX { get; set; }

    public double OffsetY { get; set; }
}

internal sealed class C4Model : IDiagramModel
{
    public string? Title { get; set; }

    public C4Boundary Root { get; } = new("", "", "", false);

    public List<C4Relation> Relations { get; } = [];

    public Dictionary<string, C4Element> Elements { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, C4Boundary> Boundaries { get; } = new(StringComparer.Ordinal);

    public int ShapesInRow { get; set; } = 4;

    public int BoundariesInRow { get; set; } = 2;

    public DiagramScene Render(RenderContext context, DiagramInfo info) => C4Renderer.Render(this, context, info);
}

internal static partial class C4Parser
{
    private const int MaxDepth = 32;

    [GeneratedRegex(@"^(?<name>\w+)\s*\((?<args>.*)\)\s*(?<open>\{)?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Call();

    public static C4Model Parse(Source source)
    {
        var model = new C4Model();
        var stack = new Stack<C4Boundary>();
        stack.Push(model.Root);
        var count = 0;
        foreach (var line in source.Lines)
        {
            var text = line.Trimmed;
            if (StartsWithWord(text, "title"))
            {
                model.Title = text[5..].Trim();
                continue;
            }

            if (text == "}")
            {
                if (stack.Count == 1)
                {
                    throw Error(line, "Unexpected '}'.");
                }

                stack.Pop();
                continue;
            }

            if (text == "{" || StartsWithWord(text, "AddElementTag") || StartsWithWord(text, "AddRelTag"))
            {
                continue;
            }

            var call = Call().Match(text);
            if (!call.Success)
            {
                throw Error(line, $"Unrecognized statement '{Source.Truncate(text)}'.");
            }

            CheckCount(++count, MermaidEngine.MaxElements, line.Number, "statements");
            var name = call.Groups["name"].Value;
            var (args, named) = Arguments(call.Groups["args"].Value);
            string Arg(int i) => i < args.Count ? args[i] : "";
            string? Optional(int i, string key) => named.TryGetValue(key, out var v) ? v : i < args.Count && args[i].Length > 0 ? args[i] : null;
            if (ElementKind(name) is { } kind)
            {
                if (args.Count < 2)
                {
                    throw Error(line, $"{name} needs at least an alias and a label.");
                }

                var element = new C4Element(Arg(0), kind.Type, kind.Form, Arg(1));
                if (kind.HasTechnology)
                {
                    element.Technology = Optional(2, "techn");
                    element.Description = Optional(3, "descr");
                }
                else
                {
                    element.Description = Optional(2, "descr");
                }

                model.Elements[element.Alias] = element;
                stack.Peek().Elements.Add(element);
                continue;
            }

            if (BoundaryKind(name) is { } boundaryKind)
            {
                if (args.Count < 2)
                {
                    throw Error(line, $"{name} needs an alias and a label.");
                }

                var type = boundaryKind.Type ?? Optional(2, "type") ?? (boundaryKind.IsNode ? "" : "system");
                var boundary = new C4Boundary(Arg(0), Arg(1), type, boundaryKind.IsNode)
                {
                    Description = boundaryKind.IsNode ? Optional(3, "descr") : null
                };
                model.Boundaries[boundary.Alias] = boundary;
                stack.Peek().Children.Add(boundary);
                if (call.Groups["open"].Success)
                {
                    if (stack.Count > MaxDepth)
                    {
                        throw new MermaidParseException(MermaidErrorCode.TooDeep, $"Boundaries are nested more than {MaxDepth} levels deep.", line.Number);
                    }

                    stack.Push(boundary);
                }

                continue;
            }

            switch (name)
            {
                case "Rel" or "Rel_U" or "Rel_Up" or "Rel_D" or "Rel_Down" or "Rel_L" or "Rel_Left" or "Rel_R" or "Rel_Right" or "BiRel" or "Rel_Back" or "RelIndex":
                {
                    var shift = name == "RelIndex" ? 1 : 0;
                    if (args.Count < 3 + shift)
                    {
                        throw Error(line, $"{name} needs from, to and a label.");
                    }

                    var (from, to) = name == "Rel_Back" ? (Arg(1 + shift), Arg(shift)) : (Arg(shift), Arg(1 + shift));
                    model.Relations.Add(new C4Relation(from, to, Arg(2 + shift), name == "BiRel") { Technology = Optional(3 + shift, "techn") });
                    break;
                }

                case "UpdateElementStyle":
                case "UpdateBoundaryStyle":
                {
                    var target = Arg(0);
                    if (model.Elements.TryGetValue(target, out var element))
                    {
                        element.Background = Color(named, "bgColor") ?? element.Background;
                        element.FontColor = Color(named, "fontColor") ?? element.FontColor;
                        element.Border = Color(named, "borderColor") ?? element.Border;
                    }
                    else if (model.Boundaries.TryGetValue(target, out var boundary))
                    {
                        boundary.Background = Color(named, "bgColor") ?? boundary.Background;
                        boundary.FontColor = Color(named, "fontColor") ?? boundary.FontColor;
                        boundary.Border = Color(named, "borderColor") ?? boundary.Border;
                    }

                    break;
                }

                case "UpdateRelStyle":
                    foreach (var relation in model.Relations.Where(r => r.From == Arg(0) && r.To == Arg(1)))
                    {
                        relation.TextColor = Color(named, "textColor") ?? relation.TextColor;
                        relation.LineColor = Color(named, "lineColor") ?? relation.LineColor;
                        relation.OffsetX = named.TryGetValue("offsetX", out var ox) && TryNumber(ox, out var x) ? Math.Clamp(x, -1000, 1000) : relation.OffsetX;
                        relation.OffsetY = named.TryGetValue("offsetY", out var oy) && TryNumber(oy, out var y) ? Math.Clamp(y, -1000, 1000) : relation.OffsetY;
                    }

                    break;
                case "UpdateLayoutConfig":
                    if (named.TryGetValue("c4ShapeInRow", out var shapes) && int.TryParse(shapes, out var s) && s > 0)
                    {
                        model.ShapesInRow = Math.Min(s, 50);
                    }

                    if (named.TryGetValue("c4BoundaryInRow", out var boundaries) && int.TryParse(boundaries, out var b) && b > 0)
                    {
                        model.BoundariesInRow = Math.Min(b, 50);
                    }

                    break;
                default:
                    throw Error(line, $"Unknown C4 statement '{Source.Truncate(name)}'.");
            }
        }

        foreach (var relation in model.Relations)
        {
            foreach (var end in new[] { relation.From, relation.To })
            {
                if (!model.Elements.ContainsKey(end) && !model.Boundaries.ContainsKey(end))
                {
                    throw new MermaidParseException(MermaidErrorCode.UnknownReference, $"Relationship refers to '{Source.Truncate(end)}', which is not declared.", source.HeaderLine);
                }
            }
        }

        return model;
    }

    private static string? Color(Dictionary<string, string> named, string key) =>
        named.TryGetValue(key, out var value) && Theming.Color.TryParse(value, out var color) ? color.ToString() : null;

    private static (List<string> Positional, Dictionary<string, string> Named) Arguments(string text)
    {
        var positional = new List<string>();
        var named = new Dictionary<string, string>(StringComparer.Ordinal);
        if (text.Trim().Length == 0)
        {
            return (positional, named);
        }

        foreach (var raw in SplitTopLevel(text, ','))
        {
            var part = raw.Trim();
            if (part.StartsWith('$') && part.IndexOf('=') is var eq and > 1)
            {
                named[part[1..eq].Trim()] = Unquote(part[(eq + 1)..]);
            }
            else
            {
                positional.Add(Unquote(part));
            }
        }

        return (positional, named);
    }

    private static (string Type, C4ShapeForm Form, bool HasTechnology)? ElementKind(string name)
    {
        var external = name.EndsWith("_Ext", StringComparison.Ordinal);
        var core = external ? name[..^4] : name;
        (string Type, C4ShapeForm Form, bool Techn)? kind = core switch
        {
            "Person" => ("person", C4ShapeForm.Box, false),
            "System" => ("system", C4ShapeForm.Box, false),
            "SystemDb" => ("system_db", C4ShapeForm.Database, false),
            "SystemQueue" => ("system_queue", C4ShapeForm.Queue, false),
            "Container" => ("container", C4ShapeForm.Box, true),
            "ContainerDb" => ("container_db", C4ShapeForm.Database, true),
            "ContainerQueue" => ("container_queue", C4ShapeForm.Queue, true),
            "Component" => ("component", C4ShapeForm.Box, true),
            "ComponentDb" => ("component_db", C4ShapeForm.Database, true),
            "ComponentQueue" => ("component_queue", C4ShapeForm.Queue, true),
            _ => null
        };
        return kind is { } k ? ((external ? "external_" : "") + k.Type, k.Form, k.Techn) : null;
    }

    private static (string? Type, bool IsNode)? BoundaryKind(string name) => name switch
    {
        "Boundary" => (null, false),
        "Enterprise_Boundary" => ("ENTERPRISE", false),
        "System_Boundary" => ("SYSTEM", false),
        "Container_Boundary" => ("CONTAINER", false),
        "Deployment_Node" or "Node" or "Node_L" or "Node_R" => (null, true),
        _ => null
    };
}

internal static class C4Renderer
{
    private const double ShapeWidth = 216;
    private const double ShapeMargin = 50;
    private const double Inset = 20;
    private const double PersonIcon = 48;

    private sealed record ShapeText(MeasuredText Type, MeasuredText Label, MeasuredText? Technology, MeasuredText? Description, double Height);

    public static DiagramScene Render(C4Model model, RenderContext context, DiagramInfo info)
    {
        var theme = context.Theme;
        var texts = model.Elements.Values.ToDictionary(e => e.Alias, e => Measure(e, context), StringComparer.Ordinal);
        LayoutBoundary(model.Root, 0, 0, model, texts, context, isRoot: true);

        var canvas = context.CreateCanvas();
        DrawBoundaries(model.Root, canvas, context);
        foreach (var element in model.Elements.Values)
        {
            DrawElement(element, texts[element.Alias], canvas, context);
        }

        var lineColor = theme.Hex("c4RelationColor", "#444444");
        var background = theme.Hex("background", "#FFFFFF");
        foreach (var relation in model.Relations)
        {
            var from = Bounds(model, relation.From);
            var to = Bounds(model, relation.To);
            var start = LayeredLayout.IntersectRect(from, new Point(from.CenterX, from.CenterY), new Point(to.CenterX, to.CenterY));
            var end = LayeredLayout.IntersectRect(to, new Point(to.CenterX, to.CenterY), new Point(from.CenterX, from.CenterY));
            var color = relation.LineColor ?? lineColor;
            var visual = new EdgeVisual(color, 1, null, relation.Both ? Marker.Arrow : Marker.None, Marker.Arrow, CurveKind.Linear);
            EdgeDrawer.Draw(canvas, [start, end], visual, null, background);

            var textColor = relation.TextColor ?? theme.Hex("c4RelationTextColor", theme.Hex("textColor"));
            var (mx, my) = ((start.X + end.X) / 2 + relation.OffsetX, (start.Y + end.Y) / 2 + relation.OffsetY);
            var label = context.Measure(relation.Label, 12, 200, forceWrap: true);
            canvas.Label(label, mx, my - label.Height / 2, TextAnchor.Middle, textColor, bold: true);
            if (relation.Technology is { Length: > 0 } technology)
            {
                canvas.Text($"[{technology}]", mx, my + label.Height / 2 + 11, 11, TextAnchor.Middle, textColor, italic: true);
            }
        }

        if (!string.IsNullOrWhiteSpace(info.Title))
        {
            var bounds = canvas.Extent();
            canvas.Text(info.Title, (bounds.MinX + bounds.MaxX) / 2, bounds.MinY - 14, 16, TextAnchor.Middle, theme.Hex("textColor"), bold: true);
        }

        return canvas.Finish(DiagramKind.C4, info.Title, info.AccDescription, 20, null);
    }

    private static Rect Bounds(C4Model model, string alias) =>
        model.Elements.TryGetValue(alias, out var element) ? element.Bounds : model.Boundaries[alias].Bounds;

    private static ShapeText Measure(C4Element element, RenderContext context)
    {
        var width = ShapeWidth - 2 * 10;
        var type = context.MeasurePlain($"«{element.Type}»", 12);
        var label = context.Measure(RichText.Parse(element.Label).Bold(), 16, width, forceWrap: true);
        var technology = element.Technology is { Length: > 0 } t ? context.Measure($"[{t}]", 12, width, forceWrap: true) : null;
        var description = element.Description is { Length: > 0 } d ? context.Measure(d, 14, width, forceWrap: true) : null;
        var height = 10 + (element.Type.EndsWith("person", StringComparison.Ordinal) ? PersonIcon + 4 : 0) + type.Height + 4 + label.Height
            + (technology is null ? 0 : technology.Height + 2) + (description is null ? 0 : description.Height + 6) + 10;
        return new ShapeText(type, label, technology, description, Math.Max(60, height));
    }

    /// <summary>
    /// Mermaid's C4 layout: shapes on a grid of <see cref="C4Model.ShapesInRow"/> columns, then nested
    /// boundaries below them in rows of <see cref="C4Model.BoundariesInRow"/>.
    /// </summary>
    private static Rect LayoutBoundary(C4Boundary boundary, double x, double y, C4Model model, Dictionary<string, ShapeText> texts, RenderContext context, bool isRoot)
    {
        var header = 0.0;
        if (!isRoot)
        {
            header = context.MeasurePlain(boundary.Label, 14, bold: true).Height + (boundary.Type.Length > 0 ? 16 : 0)
                + (boundary.Description is { Length: > 0 } ? 16 : 0) + 10;
        }

        var left = x + (isRoot ? 0 : Inset);
        var cursorY = y + header + (isRoot ? 0 : 10);
        var right = left;
        for (var row = 0; row * model.ShapesInRow < boundary.Elements.Count; row++)
        {
            var items = boundary.Elements.Skip(row * model.ShapesInRow).Take(model.ShapesInRow).ToList();
            var rowHeight = items.Max(e => texts[e.Alias].Height);
            for (var i = 0; i < items.Count; i++)
            {
                var ex = left + i * (ShapeWidth + ShapeMargin);
                items[i].Bounds = new Rect(ex, cursorY, ShapeWidth, texts[items[i].Alias].Height);
                right = Math.Max(right, ex + ShapeWidth);
            }

            cursorY += rowHeight + ShapeMargin;
        }

        for (var row = 0; row * model.BoundariesInRow < boundary.Children.Count; row++)
        {
            var items = boundary.Children.Skip(row * model.BoundariesInRow).Take(model.BoundariesInRow).ToList();
            var bx = left;
            var rowHeight = 0.0;
            foreach (var child in items)
            {
                var bounds = LayoutBoundary(child, bx, cursorY, model, texts, context, isRoot: false);
                bx = bounds.Right + ShapeMargin;
                right = Math.Max(right, bounds.Right);
                rowHeight = Math.Max(rowHeight, bounds.Height);
            }

            cursorY += rowHeight + ShapeMargin;
        }

        var bottom = cursorY - ShapeMargin + (isRoot ? 0 : Inset);
        var width = Math.Max(right - x + (isRoot ? 0 : Inset), isRoot ? 0 : context.MeasurePlain(boundary.Label, 14, bold: true).Width + 2 * Inset);
        boundary.Bounds = new Rect(x, y, width, Math.Max(bottom - y, header + 2 * Inset));
        return boundary.Bounds;
    }

    private static void DrawBoundaries(C4Boundary boundary, Canvas canvas, RenderContext context)
    {
        var theme = context.Theme;
        foreach (var child in boundary.Children)
        {
            var r = child.Bounds;
            var stroke = child.Border ?? theme.Hex("c4BoundaryBorderColor", "#444444");
            canvas.Rect(r.X, r.Y, r.Width, r.Height, child.Background, stroke, 1, 2.5, child.IsNode ? null : [7, 7]);
            var color = child.FontColor ?? theme.Hex("textColor");
            var label = context.MeasurePlain(child.Label, 14, bold: true);
            var y = r.Y + 10;
            canvas.Label(label, r.X + 10, y, TextAnchor.Start, color, bold: true);
            y += label.Height;
            if (child.Type.Length > 0)
            {
                canvas.Text($"[{child.Type}]", r.X + 10, y + 12, 12, TextAnchor.Start, color);
                y += 16;
            }

            if (child.Description is { Length: > 0 } description)
            {
                canvas.Text(description, r.X + 10, y + 12, 12, TextAnchor.Start, color, italic: true);
            }

            DrawBoundaries(child, canvas, context);
        }
    }

    private static void DrawElement(C4Element element, ShapeText text, Canvas canvas, RenderContext context)
    {
        var theme = context.Theme;
        var (fill, border, color) = Palette(element.Type);
        fill = element.Background ?? theme.Hex($"c4.{element.Type}.bg", fill);
        border = element.Border ?? theme.Hex($"c4.{element.Type}.border", border);
        color = element.FontColor ?? color;
        var r = element.Bounds;
        switch (element.Form)
        {
            case C4ShapeForm.Database:
                Shapes.Draw(canvas, NodeShape.Cylinder, r, fill, border, 0.5);
                break;
            case C4ShapeForm.Queue:
                Shapes.Draw(canvas, NodeShape.HorizontalCylinder, r, fill, border, 0.5);
                break;
            default:
                canvas.Rect(r.X, r.Y, r.Width, r.Height, fill, border, 0.5, 2.5);
                break;
        }

        var y = r.Y + 10;
        if (element.Type.EndsWith("person", StringComparison.Ordinal))
        {
            Person(canvas, r.CenterX, y, color);
            y += PersonIcon + 4;
        }

        canvas.Label(text.Type, r.CenterX, y, TextAnchor.Middle, color);
        y += text.Type.Height + 4;
        canvas.Label(text.Label, r.CenterX, y, TextAnchor.Middle, color, bold: true);
        y += text.Label.Height;
        if (text.Technology is { } technology)
        {
            canvas.Label(technology, r.CenterX, y + 2, TextAnchor.Middle, color);
            y += technology.Height + 2;
        }

        if (text.Description is { } description)
        {
            canvas.Label(description, r.CenterX, y + 6, TextAnchor.Middle, color);
        }
    }

    /// <summary>Default C4-PlantUML colors that Mermaid uses: fill, border, text.</summary>
    private static (string Fill, string Border, string Text) Palette(string type) => type switch
    {
        "person" => ("#08427B", "#073B6F", "#FFFFFF"),
        "external_person" => ("#686868", "#8A8A8A", "#FFFFFF"),
        "system" or "system_db" or "system_queue" => ("#1168BD", "#3C7FC0", "#FFFFFF"),
        "external_system" or "external_system_db" or "external_system_queue" => ("#999999", "#8A8A8A", "#FFFFFF"),
        "container" or "container_db" or "container_queue" => ("#438DD5", "#3C7FC0", "#FFFFFF"),
        "external_container" or "external_container_db" or "external_container_queue" => ("#B3B3B3", "#A6A6A6", "#FFFFFF"),
        "component" or "component_db" or "component_queue" => ("#85BBF0", "#78A8D8", "#000000"),
        _ => ("#CCCCCC", "#BFBFBF", "#000000")
    };

    /// <summary>Head and shoulders, standing in for the bitmap icon Mermaid embeds.</summary>
    private static void Person(Canvas canvas, double cx, double top, string color)
    {
        canvas.Circle(cx, top + 13, 11, color, null, 0);
        var shoulders = new PathGeometry().MoveTo(cx - 20, top + PersonIcon).LineTo(cx - 20, top + 38);
        shoulders.ArcTo(cx - 10, top + 38, 10, 10, Math.PI, Math.PI / 2).LineTo(cx + 10, top + 28);
        shoulders.ArcTo(cx + 10, top + 38, 10, 10, -Math.PI / 2, Math.PI / 2).LineTo(cx + 20, top + PersonIcon).Close();
        canvas.Shape(shoulders, color, null, 0);
    }
}
