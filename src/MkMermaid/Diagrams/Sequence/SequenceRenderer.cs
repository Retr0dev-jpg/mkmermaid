using MkMermaid.Rendering;
using MkMermaid.Scene;
using MkMermaid.Text;
using MkMermaid.Theming;

namespace MkMermaid.Diagrams.Sequence;

internal static class SequenceRenderer
{
    private const double ActivationWidth = 10;
    private const double IconHeight = 45;

    private sealed class Frame(BlockStartEvent start, double top)
    {
        public BlockStartEvent Start { get; } = start;

        public double Top { get; } = top;

        public List<(double Y, string Label)> Dividers { get; } = [];

        public double MinX { get; set; } = double.PositiveInfinity;

        public double MaxX { get; set; } = double.NegativeInfinity;

        public void Extend(double a, double b)
        {
            MinX = Math.Min(MinX, Math.Min(a, b));
            MaxX = Math.Max(MaxX, Math.Max(a, b));
        }
    }

    public static DiagramScene Render(SequenceModel model, RenderContext context, DiagramInfo info)
    {
        var theme = context.Theme;
        var config = context.Config;
        var baseWidth = config.Number("sequence", "width", 150);
        var baseHeight = config.Number("sequence", "height", 65);
        var actorMargin = config.Number("sequence", "actorMargin", 50);
        var boxMargin = config.Number("sequence", "boxMargin", 10);
        var noteMargin = config.Number("sequence", "noteMargin", 10);
        var mirror = config.Flag("sequence", "mirrorActors", true);
        var wrapAll = config.Flag("sequence", "wrap", false);
        var actorFont = config.Number("sequence", "actorFontSize", 14);
        var messageFont = config.Number("sequence", "messageFontSize", 16);
        var noteFont = config.Number("sequence", "noteFontSize", 14);
        var showNumbers = config.Flag("sequence", "showSequenceNumbers", false);

        var actors = model.Participants.Values.OrderBy(p => p.Order).ToList();
        var index = actors.Select((p, i) => (p.Id, i)).ToDictionary(t => t.Id, t => t.i, StringComparer.Ordinal);
        var labels = actors.Select(p => context.Measure(p.Label, actorFont, wrapAll ? baseWidth - 20 : double.PositiveInfinity, wrapAll)).ToList();
        var widths = labels.Select(l => Math.Max(baseWidth, l.Width + 20)).ToList();
        var heights = actors.Select((p, i) => p.Kind is ParticipantKind.Participant or ParticipantKind.Collections or ParticipantKind.Queue or ParticipantKind.Database
            ? Math.Max(baseHeight, labels[i].Height + 20)
            : Math.Max(baseHeight, IconHeight + 2 + labels[i].Height)).ToList();
        var actorHeight = heights.Count == 0 ? baseHeight : heights.Max();

        MeasuredText MeasureText(string text, double size, double wrapWidth)
        {
            var wrap = wrapAll;
            if (text.StartsWith("wrap:", StringComparison.OrdinalIgnoreCase))
            {
                wrap = true;
                text = text[5..];
            }
            else if (text.StartsWith("nowrap:", StringComparison.OrdinalIgnoreCase))
            {
                wrap = false;
                text = text[7..];
            }

            return context.Measure(text, size, wrap ? wrapWidth : double.PositiveInfinity, wrap);
        }

        // Horizontal placement: each actor sits as far left as its neighbours and the content between them allow.
        var constraints = new List<(int Lo, int Hi, double Distance)>();
        var numbering = showNumbers;
        foreach (var e in model.Events)
        {
            switch (e)
            {
                case AutonumberEvent a:
                    numbering = !a.Off;
                    break;
                case MessageEvent m:
                    var label = MeasureText(m.Text, messageFont, baseWidth + actorMargin);
                    var i = index[m.From];
                    var j = index[m.To];
                    if (i == j)
                    {
                        if (i + 1 < actors.Count)
                        {
                            constraints.Add((i, i + 1, Math.Max(label.Width / 2, 60) + 20));
                        }

                        if (i > 0)
                        {
                            constraints.Add((i - 1, i, label.Width / 2 + 20));
                        }
                    }
                    else
                    {
                        constraints.Add((Math.Min(i, j), Math.Max(i, j), label.Width + 30 + (numbering ? 20 : 0)));
                    }

                    break;
                case NoteEvent n:
                    var note = MeasureText(n.Text, noteFont, baseWidth);
                    var width = Math.Max(baseWidth, note.Width + 2 * noteMargin);
                    var k = index[n.First];
                    switch (n.Placement)
                    {
                        case NotePlacement.RightOf when k + 1 < actors.Count:
                            constraints.Add((k, k + 1, actorMargin / 2 + width + 10));
                            break;
                        case NotePlacement.LeftOf when k > 0:
                            constraints.Add((k - 1, k, actorMargin / 2 + width + 10));
                            break;
                        case NotePlacement.Over when n.Second is { } second && index[second] != k:
                            constraints.Add((Math.Min(k, index[second]), Math.Max(k, index[second]), note.Width + 2 * noteMargin - actorMargin));
                            break;
                    }

                    break;
            }
        }

        var centers = new double[actors.Count];
        for (var k = 0; k < actors.Count; k++)
        {
            var c = widths[k] / 2;
            if (k > 0)
            {
                c = centers[k - 1] + widths[k - 1] / 2 + actorMargin + widths[k] / 2;
                if (actors[k].Box != actors[k - 1].Box && (actors[k].Box >= 0 || actors[k - 1].Box >= 0))
                {
                    c += 2 * boxMargin;
                }
            }

            foreach (var (lo, hi, distance) in constraints)
            {
                if (hi == k)
                {
                    c = Math.Max(c, centers[lo] + distance);
                }
            }

            centers[k] = c;
        }

        var back = context.CreateCanvas();
        var lines = context.CreateCanvas();
        var front = context.CreateCanvas();
        var background = theme.Hex("background", "#FFFFFF");
        var signal = theme.Hex("signalColor");
        var signalText = theme.Hex("signalTextColor");

        var boxLabels = model.Boxes.Select(b => b.Label is null ? null : context.Measure(b.Label, messageFont)).ToList();
        var actorTop = boxLabels.Any(l => l is not null) ? boxLabels.Max(l => l?.Height ?? 0) + 2 * boxMargin : 0;
        var cursor = actorTop + actorHeight;
        var lifelineStart = new double[actors.Count];
        var lifelineEnd = new double?[actors.Count];
        var drawn = new bool[actors.Count];
        var pendingDestroy = new HashSet<int>();
        var activations = actors.Select(_ => new Stack<double>()).ToArray();
        var activationRects = new List<(int Actor, int Level, double Top, double Bottom)>();
        var frames = new Stack<Frame>();
        var number = 1;
        var step = 1;
        numbering = showNumbers;

        for (var k = 0; k < actors.Count; k++)
        {
            if (!actors[k].Created)
            {
                DrawActor(front, actors[k], labels[k], centers[k], widths[k], actorTop, actorHeight, theme);
                drawn[k] = true;
                lifelineStart[k] = actorTop + actorHeight;
            }
        }

        void Activate(int actor, double y) => activations[actor].Push(y);

        void Deactivate(int actor, double y)
        {
            if (activations[actor].TryPop(out var start))
            {
                activationRects.Add((actor, activations[actor].Count, start, y));
            }
        }

        double Edge(int actor, bool rightSide)
        {
            var depth = activations[actor].Count;
            if (depth == 0)
            {
                return centers[actor];
            }

            var left = centers[actor] - ActivationWidth / 2 + (depth - 1) * ActivationWidth / 2;
            return rightSide ? left + ActivationWidth : left;
        }

        void ExtendFrame(double a, double b)
        {
            if (frames.TryPeek(out var frame))
            {
                frame.Extend(a, b);
            }
        }

        foreach (var e in model.Events)
        {
            switch (e)
            {
                case AutonumberEvent a:
                    numbering = !a.Off;
                    number = a.Start;
                    step = a.Step;
                    break;
                case ActivationEvent a:
                    if (a.On)
                    {
                        Activate(index[a.Actor], cursor);
                    }
                    else
                    {
                        Deactivate(index[a.Actor], cursor);
                    }

                    break;
                case DestroyEvent d:
                    pendingDestroy.Add(index[d.Actor]);
                    break;
                case CreateEvent:
                    break;
                case MessageEvent m:
                {
                    var from = index[m.From];
                    var to = index[m.To];
                    var label = MeasureText(m.Text, messageFont, Math.Max(baseWidth, Math.Abs(centers[to] - centers[from]) - 20));
                    var top = cursor + boxMargin;
                    var lineY = top + (label.Lines.Count > 0 ? label.Height + 4 : 10);
                    var creating = actors[to].Created && !drawn[to];
                    if (m.ActivateTarget)
                    {
                        Activate(to, lineY);
                    }

                    if (from == to)
                    {
                        var x = Edge(from, true);
                        var loop = new PathGeometry().MoveTo(x, lineY).CubicTo(x + 60, lineY - 10, x + 60, lineY + 30, x, lineY + 20);
                        front.Shape(loop, null, signal, 1.5, m.Dotted ? [3, 3] : null);
                        DrawHead(front, m.Head, new Layout.Point(x, lineY + 20), -1, 0.2, signal, background);
                        front.Label(label, centers[from], top, TextAnchor.Middle, signalText);
                        ExtendFrame(x - label.Width / 2, x + Math.Max(60, label.Width / 2));
                        if (numbering)
                        {
                            DrawNumber(front, context, number, x, lineY, theme);
                        }

                        cursor = lineY + 30;
                    }
                    else
                    {
                        var rightward = centers[to] > centers[from];
                        var startX = Edge(from, rightward);
                        var endX = creating ? centers[to] + (rightward ? -widths[to] / 2 : widths[to] / 2) : Edge(to, !rightward);
                        var direction = rightward ? 1 : -1;
                        var shaft = new List<Layout.Point> { new(startX, lineY), new(endX, lineY) };
                        Curves.ShortenEnd(shaft, Markers.Inset(m.Head));
                        if (m.Both)
                        {
                            Curves.ShortenStart(shaft, Markers.Inset(m.Head));
                        }

                        front.Line(shaft[0].X, lineY, shaft[1].X, lineY, signal, 1.5, m.Dotted ? [3, 3] : null);
                        DrawHead(front, m.Head, new Layout.Point(endX, lineY), direction, 0, signal, background);
                        if (m.Both)
                        {
                            DrawHead(front, m.Head, new Layout.Point(startX, lineY), -direction, 0, signal, background);
                        }

                        front.Label(label, (startX + endX) / 2, top, TextAnchor.Middle, signalText);
                        ExtendFrame(Math.Min(startX, endX), Math.Max(startX, endX));
                        if (numbering)
                        {
                            DrawNumber(front, context, number, startX, lineY, theme);
                        }

                        cursor = lineY + 12;
                    }

                    if (creating)
                    {
                        var boxTop = lineY - actorHeight / 2;
                        DrawActor(front, actors[to], labels[to], centers[to], widths[to], boxTop, actorHeight, theme);
                        drawn[to] = true;
                        lifelineStart[to] = boxTop + actorHeight;
                        cursor = Math.Max(cursor, boxTop + actorHeight + 4);
                    }

                    if (numbering)
                    {
                        number += step;
                    }

                    if (m.DeactivateSource)
                    {
                        Deactivate(from, lineY);
                    }

                    foreach (var actor in new[] { from, to })
                    {
                        if (pendingDestroy.Remove(actor))
                        {
                            while (activations[actor].Count > 0)
                            {
                                Deactivate(actor, lineY);
                            }

                            lifelineEnd[actor] = lineY;
                            DrawCross(front, centers[actor], lineY, signal);
                        }
                    }

                    break;
                }

                case NoteEvent n:
                {
                    var label = MeasureText(n.Text, noteFont, baseWidth);
                    var width = Math.Max(baseWidth, label.Width + 2 * noteMargin);
                    var height = label.Height + 2 * noteMargin;
                    var first = index[n.First];
                    double left;
                    switch (n.Placement)
                    {
                        case NotePlacement.RightOf:
                            left = centers[first] + actorMargin / 2;
                            break;
                        case NotePlacement.LeftOf:
                            left = centers[first] - actorMargin / 2 - width;
                            break;
                        default:
                            if (n.Second is { } other && index[other] != first)
                            {
                                var a = Math.Min(centers[first], centers[index[other]]);
                                var b = Math.Max(centers[first], centers[index[other]]);
                                width = Math.Max(b - a + actorMargin, label.Width + 2 * noteMargin);
                                left = (a + b) / 2 - width / 2;
                            }
                            else
                            {
                                left = centers[first] - width / 2;
                            }

                            break;
                    }

                    var top = cursor + boxMargin;
                    front.Rect(left, top, width, height, theme.Hex("noteBkgColor"), theme.Hex("noteBorderColor"));
                    front.Label(label, left + width / 2, top + noteMargin, TextAnchor.Middle, theme.Hex("noteTextColor"));
                    ExtendFrame(left, left + width);
                    cursor = top + height;
                    break;
                }

                case BlockStartEvent b:
                {
                    var frame = new Frame(b, cursor + boxMargin);
                    frames.Push(frame);
                    var header = b.Kind == BlockKind.Rect ? 0 : Math.Max(20, context.Measure(b.Label, messageFont).Height) + 4;
                    cursor = frame.Top + header;
                    break;
                }

                case BlockDividerEvent d:
                {
                    if (frames.TryPeek(out var frame))
                    {
                        var y = cursor + boxMargin;
                        frame.Dividers.Add((y, d.Label));
                        cursor = y + Math.Max(20, context.Measure(d.Label, messageFont).Height) + 4;
                    }

                    break;
                }

                case BlockEndEvent:
                {
                    if (!frames.TryPop(out var frame))
                    {
                        break;
                    }

                    var bottom = cursor + boxMargin;
                    if (double.IsInfinity(frame.MinX))
                    {
                        frame.Extend(centers.Length > 0 ? centers.Min() : 0, centers.Length > 0 ? centers.Max() : 0);
                    }

                    var left = frame.MinX - boxMargin;
                    var right = frame.MaxX + boxMargin;
                    DrawFrame(frame, left, right, bottom, frame.Start.Kind == BlockKind.Rect ? back : front, context, messageFont, out right);
                    ExtendFrame(left, right);
                    cursor = bottom;
                    break;
                }
            }
        }

        cursor += 2 * boxMargin;
        for (var k = 0; k < actors.Count; k++)
        {
            while (activations[k].Count > 0)
            {
                Deactivate(k, cursor - boxMargin);
            }
        }

        var bottomTop = cursor;
        for (var k = 0; k < actors.Count; k++)
        {
            if (!drawn[k])
            {
                continue;
            }

            var end = lifelineEnd[k] ?? bottomTop;
            lines.Line(centers[k], lifelineStart[k], centers[k], end, theme.Hex("actorLineColor"), 0.5);
            if (mirror && lifelineEnd[k] is null)
            {
                DrawActor(front, actors[k], labels[k], centers[k], widths[k], bottomTop, actorHeight, theme);
            }
        }

        foreach (var (actor, level, top, bottom) in activationRects.OrderBy(r => r.Level))
        {
            var x = centers[actor] - ActivationWidth / 2 + level * ActivationWidth / 2;
            lines.Rect(x, top, ActivationWidth, Math.Max(2, bottom - top), theme.Hex("activationBkgColor"), theme.Hex("activationBorderColor"));
        }

        var bottomEdge = mirror ? bottomTop + actorHeight : bottomTop;
        var boxes = context.CreateCanvas();
        for (var b = 0; b < model.Boxes.Count; b++)
        {
            var members = Enumerable.Range(0, actors.Count).Where(k => actors[k].Box == b).ToList();
            if (members.Count == 0)
            {
                continue;
            }

            var left = members.Min(k => centers[k] - widths[k] / 2) - boxMargin;
            var right = members.Max(k => centers[k] + widths[k] / 2) + boxMargin;
            var box = model.Boxes[b];
            var fill = box.Color is { } color && !color.Equals("transparent", StringComparison.OrdinalIgnoreCase) && Color.TryParse(color, out var parsed) ? parsed.ToString() : null;
            boxes.Rect(left, 0, right - left, bottomEdge + boxMargin, fill, fill is null ? theme.Hex("labelBoxBorderColor") : null, 0.5);
            if (boxLabels[b] is { } text)
            {
                boxes.Label(text, (left + right) / 2, boxMargin, TextAnchor.Middle, theme.Hex("textColor"));
            }
        }

        var canvas = context.CreateCanvas();
        canvas.Append(boxes);
        canvas.Append(back);
        canvas.Append(lines);
        canvas.Append(front);
        canvas.Title(info.Title, theme.Hex("textColor"));
        return canvas.Finish(DiagramKind.Sequence, info.Title, info.AccDescription, 10, null);
    }

    private static void DrawHead(Canvas canvas, Marker head, Layout.Point tip, double dx, double dy, string stroke, string background)
    {
        var length = Math.Sqrt(dx * dx + dy * dy);
        Markers.Draw(canvas, head, tip, dx / length, dy / length, stroke, background, 1.5);
    }

    private static void DrawNumber(Canvas canvas, RenderContext context, int number, double x, double y, Theme theme)
    {
        var text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var radius = Math.Max(8, context.TextWidth(text, 12) / 2 + 3);
        canvas.Circle(x, y, radius, theme.Hex("signalColor"), null);
        canvas.Text(text, x, y + 4, 12, TextAnchor.Middle, theme.Hex("sequenceNumberColor"), bold: true);
    }

    private static void DrawCross(Canvas canvas, double x, double y, string stroke)
    {
        canvas.Line(x - 9, y - 9, x + 9, y + 9, stroke, 2);
        canvas.Line(x - 9, y + 9, x + 9, y - 9, stroke, 2);
    }

    private static void DrawFrame(Frame frame, double left, double right, double bottom, Canvas canvas, RenderContext context, double fontSize, out double finalRight)
    {
        var theme = context.Theme;
        var start = frame.Start;
        if (start.Kind == BlockKind.Rect)
        {
            var fill = Color.TryParse(start.Label, out var color) ? color.ToString() : theme.C("labelBoxBkgColor").WithAlpha(0.3).ToString();
            canvas.Rect(left, frame.Top, right - left, bottom - frame.Top, fill, null);
            finalRight = right;
            return;
        }

        var keyword = start.Kind.ToString().ToLowerInvariant();
        var keywordText = context.MeasurePlain(keyword, fontSize * 0.875, bold: true);
        var boxWidth = Math.Max(50, keywordText.Width + 20);
        var boxHeight = Math.Max(20, keywordText.Height) + 2;
        var condition = start.Label.Length > 0 ? context.Measure("[" + start.Label + "]", fontSize) : null;
        right = Math.Max(right, left + boxWidth + (condition?.Width ?? 0) + 30);
        finalRight = right;
        var border = theme.Hex("labelBoxBorderColor");
        IReadOnlyList<double> dash = [2, 2];
        canvas.Rect(left, frame.Top, right - left, bottom - frame.Top, null, border, 2, dash: dash);
        canvas.Shape(PathGeometry.Polygon([
            (left, frame.Top), (left + boxWidth, frame.Top), (left + boxWidth, frame.Top + boxHeight - 7),
            (left + boxWidth - 8.4, frame.Top + boxHeight), (left, frame.Top + boxHeight)]), theme.Hex("labelBoxBkgColor"), border);
        canvas.LabelCentered(keywordText, left + boxWidth / 2, frame.Top + boxHeight / 2, theme.Hex("labelTextColor"), bold: true);
        var loopText = theme.Hex("loopTextColor");
        if (condition is not null)
        {
            var center = Math.Max((left + right) / 2, left + boxWidth + 10 + condition.Width / 2);
            canvas.Label(condition, center, frame.Top + 1, TextAnchor.Middle, loopText);
        }

        foreach (var (y, label) in frame.Dividers)
        {
            canvas.Line(left, y, right, y, border, 2, [3, 3]);
            if (label.Length > 0)
            {
                canvas.Label(context.Measure("[" + label + "]", fontSize), (left + right) / 2, y + 2, TextAnchor.Middle, loopText);
            }
        }
    }

    private static void DrawActor(Canvas canvas, Participant actor, MeasuredText label, double center, double width, double top, double height, Theme theme)
    {
        var fill = theme.Hex("actorBkg");
        var stroke = theme.Hex("actorBorder");
        var text = theme.Hex("actorTextColor");
        var left = center - width / 2;
        var rect = new Layout.Rect(left, top, width, height);
        switch (actor.Kind)
        {
            case ParticipantKind.Participant:
                canvas.Rect(left, top, width, height, fill, stroke, 1, 3);
                canvas.LabelCentered(label, center, top + height / 2, text);
                return;
            case ParticipantKind.Database:
                Shapes.Draw(canvas, NodeShape.Cylinder, rect, fill, stroke, 1);
                canvas.LabelCentered(label, center, top + height * 0.56, text);
                return;
            case ParticipantKind.Collections:
                Shapes.Draw(canvas, NodeShape.StackedRect, rect, fill, stroke, 1);
                canvas.LabelCentered(label, center - 3, top + height / 2 + 3, text);
                return;
            case ParticipantKind.Queue:
                Shapes.Draw(canvas, NodeShape.HorizontalCylinder, rect, fill, stroke, 1);
                canvas.LabelCentered(label, center - 6, top + height / 2, text);
                return;
        }

        var line = theme.Hex("actorBorder");
        switch (actor.Kind)
        {
            case ParticipantKind.Actor:
                canvas.Circle(center, top + 9, 8, fill, line, 1.5);
                canvas.Line(center, top + 17, center, top + 33, line, 1.5);
                canvas.Line(center - 13, top + 22, center + 13, top + 22, line, 1.5);
                canvas.Line(center, top + 33, center - 11, top + 45, line, 1.5);
                canvas.Line(center, top + 33, center + 11, top + 45, line, 1.5);
                break;
            case ParticipantKind.Boundary:
                canvas.Line(center - 20, top + 6, center - 20, top + 36, line, 1.5);
                canvas.Line(center - 20, top + 21, center - 7, top + 21, line, 1.5);
                canvas.Circle(center + 6, top + 21, 13, fill, line, 1.5);
                break;
            case ParticipantKind.Control:
                canvas.Circle(center, top + 23, 15, fill, line, 1.5);
                canvas.Shape(PathGeometry.Polyline([(center + 6, top + 3), (center - 2, top + 8), (center + 6, top + 13)]), null, line, 1.5);
                break;
            case ParticipantKind.Entity:
                canvas.Circle(center, top + 20, 15, fill, line, 1.5);
                canvas.Line(center - 15, top + 38, center + 15, top + 38, line, 1.5);
                break;
        }

        canvas.Label(label, center, top + IconHeight + 2, TextAnchor.Middle, text);
    }
}
