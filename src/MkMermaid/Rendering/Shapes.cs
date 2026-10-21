using MkMermaid.Layout;
using MkMermaid.Scene;

namespace MkMermaid.Rendering;

internal enum NodeShape
{
    Rect,
    Rounded,
    Stadium,
    Subroutine,
    Cylinder,
    Circle,
    DoubleCircle,
    Asymmetric,
    Diamond,
    Hexagon,
    LeanRight,
    LeanLeft,
    Trapezoid,
    TrapezoidAlt,
    Document,
    MultiDocument,
    Card,
    Triangle,
    FlippedTriangle,
    Delay,
    HorizontalCylinder,
    LinedCylinder,
    Flag,
    Hourglass,
    CurvedTrapezoid,
    DividedRect,
    WindowPane,
    StackedRect,
    TextOnly,
    SmallCircle,
    FramedCircle,
    FilledCircle,
    CrossedCircle,
    BowTieRect,
    ForkBar,
    TaggedRect,
    LinedRect,
    NotchedPentagon,
    Bolt,
    BraceLeft,
    BraceRight,
    Braces,
    Ellipse
}

internal static class Shapes
{
    public static NodeShape? FromName(string name) => name.Trim().ToLowerInvariant() switch
    {
        "rect" or "rectangle" or "proc" or "process" or "square" => NodeShape.Rect,
        "rounded" or "event" => NodeShape.Rounded,
        "stadium" or "pill" or "terminal" => NodeShape.Stadium,
        "fr-rect" or "subproc" or "subprocess" or "subroutine" or "framed-rectangle" => NodeShape.Subroutine,
        "cyl" or "cylinder" or "database" or "db" => NodeShape.Cylinder,
        "circle" or "circ" => NodeShape.Circle,
        "dbl-circ" or "double-circle" => NodeShape.DoubleCircle,
        "odd" => NodeShape.Asymmetric,
        "diam" or "diamond" or "decision" or "question" => NodeShape.Diamond,
        "hex" or "hexagon" or "prepare" => NodeShape.Hexagon,
        "lean-r" or "lean-right" or "in-out" => NodeShape.LeanRight,
        "lean-l" or "lean-left" or "out-in" => NodeShape.LeanLeft,
        "trap-b" or "trapezoid" or "priority" or "trapezoid-bottom" => NodeShape.Trapezoid,
        "trap-t" or "manual" or "inv-trapezoid" or "trapezoid-top" => NodeShape.TrapezoidAlt,
        "doc" or "document" => NodeShape.Document,
        "docs" or "documents" or "st-doc" or "stacked-document" => NodeShape.MultiDocument,
        "notch-rect" or "card" or "notched-rectangle" => NodeShape.Card,
        "tri" or "extract" or "triangle" => NodeShape.Triangle,
        "flip-tri" or "manual-file" or "flipped-triangle" => NodeShape.FlippedTriangle,
        "delay" or "half-rounded-rectangle" => NodeShape.Delay,
        "h-cyl" or "das" or "horizontal-cylinder" => NodeShape.HorizontalCylinder,
        "lin-cyl" or "disk" or "lined-cylinder" => NodeShape.LinedCylinder,
        "flag" or "paper-tape" => NodeShape.Flag,
        "hourglass" or "collate" => NodeShape.Hourglass,
        "curv-trap" or "display" or "curved-trapezoid" => NodeShape.CurvedTrapezoid,
        "div-rect" or "div-proc" or "divided-rectangle" or "divided-process" => NodeShape.DividedRect,
        "win-pane" or "internal-storage" or "window-pane" => NodeShape.WindowPane,
        "st-rect" or "procs" or "processes" or "stacked-rectangle" => NodeShape.StackedRect,
        "text" => NodeShape.TextOnly,
        "sm-circ" or "start" or "small-circle" => NodeShape.SmallCircle,
        "fr-circ" or "stop" or "framed-circle" => NodeShape.FramedCircle,
        "f-circ" or "junction" or "filled-circle" => NodeShape.FilledCircle,
        "cross-circ" or "summary" or "crossed-circle" => NodeShape.CrossedCircle,
        "bow-rect" or "stored-data" or "bow-tie-rectangle" => NodeShape.BowTieRect,
        "fork" or "join" => NodeShape.ForkBar,
        "tag-rect" or "tagged-rectangle" or "tag-proc" or "tagged-process" => NodeShape.TaggedRect,
        "lin-rect" or "lined-rectangle" or "lin-proc" or "lined-process" or "shaded-process" => NodeShape.LinedRect,
        "notch-pent" or "loop-limit" or "notched-pentagon" => NodeShape.NotchedPentagon,
        "bolt" or "com-link" or "lightning-bolt" => NodeShape.Bolt,
        "brace" or "brace-l" or "comment" => NodeShape.BraceLeft,
        "brace-r" => NodeShape.BraceRight,
        "braces" => NodeShape.Braces,
        "lin-doc" or "lined-document" or "tag-doc" or "tagged-document" => NodeShape.Document,
        "ellipse" => NodeShape.Ellipse,
        _ => null
    };

    /// <summary>Outer size for a label of the given size, using Mermaid's label padding.</summary>
    public static (double Width, double Height) Size(NodeShape shape, double labelWidth, double labelHeight, double padding)
    {
        var w = labelWidth + 2 * padding;
        var h = labelHeight + 2 * padding;
        switch (shape)
        {
            case NodeShape.Circle:
            case NodeShape.CrossedCircle:
                var d = Math.Max(labelWidth, labelHeight) + padding;
                return (d, d);
            case NodeShape.DoubleCircle:
                var dd = Math.Max(labelWidth, labelHeight) + padding + 10;
                return (dd, dd);
            case NodeShape.Ellipse:
                return (labelWidth * 1.42 + padding, labelHeight * 1.42 + padding);
            case NodeShape.SmallCircle or NodeShape.FilledCircle:
                return (14, 14);
            case NodeShape.FramedCircle:
                return (18, 18);
            case NodeShape.ForkBar:
                return (70, 10);
            case NodeShape.Diamond:
                var side = labelWidth + labelHeight + padding;
                return (side, side);
            case NodeShape.Hexagon:
                return (w + h / 2, h);
            case NodeShape.LeanLeft or NodeShape.LeanRight or NodeShape.Trapezoid or NodeShape.TrapezoidAlt:
                return (w + h, h);
            case NodeShape.Stadium:
                return (w + h / 2, h);
            case NodeShape.Subroutine:
                return (w + 16, h);
            case NodeShape.Cylinder or NodeShape.LinedCylinder:
                return (w, h + 2 * CylinderCap(w));
            case NodeShape.HorizontalCylinder:
                return (w + 2 * CylinderCap(h), h);
            case NodeShape.Asymmetric:
                return (w + h / 4, h);
            case NodeShape.Document or NodeShape.MultiDocument:
                return (w + (shape == NodeShape.MultiDocument ? 10 : 0), h + h / 8 + (shape == NodeShape.MultiDocument ? 10 : 0));
            case NodeShape.Triangle or NodeShape.FlippedTriangle:
                return (Math.Max(w, h) * 1.6, h * 1.6);
            case NodeShape.Hourglass:
                return (Math.Max(w, 30), Math.Max(h * 2, 30));
            case NodeShape.StackedRect:
                return (w + 10, h + 10);
            case NodeShape.WindowPane or NodeShape.DividedRect:
                return (w + 10, h + 10);
            case NodeShape.CurvedTrapezoid or NodeShape.Delay or NodeShape.BowTieRect:
                return (w + h / 2, h);
            case NodeShape.TextOnly:
                return (labelWidth + 8, labelHeight + 8);
            case NodeShape.Bolt:
                return (Math.Max(w, 40), Math.Max(h, 40));
            case NodeShape.BraceLeft or NodeShape.BraceRight or NodeShape.Braces:
                return (w + 10, h);
            default:
                return (w, h);
        }
    }

    private static double CylinderCap(double width) => Math.Min(width / 2 / (2.5 + width / 50), 14);

    /// <summary>Outline polygon used to clip edges; curved shapes are sampled.</summary>
    public static List<Point> Outline(NodeShape shape, Rect r)
    {
        var (x, y, w, h) = (r.X, r.Y, r.Width, r.Height);
        var (cx, cy) = (r.CenterX, r.CenterY);
        switch (shape)
        {
            case NodeShape.Circle or NodeShape.DoubleCircle or NodeShape.SmallCircle or NodeShape.FilledCircle or NodeShape.FramedCircle or NodeShape.CrossedCircle or NodeShape.Ellipse:
                return Enumerable.Range(0, 32).Select(i => new Point(cx + w / 2 * Math.Cos(i * Math.PI / 16), cy + h / 2 * Math.Sin(i * Math.PI / 16))).ToList();
            case NodeShape.Diamond:
                return [new(cx, y), new(x + w, cy), new(cx, y + h), new(x, cy)];
            case NodeShape.Hexagon:
                var m = h / 4;
                return [new(x + m, y), new(x + w - m, y), new(x + w, cy), new(x + w - m, y + h), new(x + m, y + h), new(x, cy)];
            case NodeShape.LeanRight:
                return [new(x + h / 2, y), new(x + w, y), new(x + w - h / 2, y + h), new(x, y + h)];
            case NodeShape.LeanLeft:
                return [new(x, y), new(x + w - h / 2, y), new(x + w, y + h), new(x + h / 2, y + h)];
            case NodeShape.Trapezoid:
                return [new(x + h / 2, y), new(x + w - h / 2, y), new(x + w, y + h), new(x, y + h)];
            case NodeShape.TrapezoidAlt:
                return [new(x, y), new(x + w, y), new(x + w - h / 2, y + h), new(x + h / 2, y + h)];
            case NodeShape.Asymmetric:
                return [new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h), new(x + h / 4, cy)];
            case NodeShape.Triangle:
                return [new(cx, y), new(x + w, y + h), new(x, y + h)];
            case NodeShape.FlippedTriangle:
                return [new(x, y), new(x + w, y), new(cx, y + h)];
            case NodeShape.Stadium:
                var pts = new List<Point>();
                var radius = h / 2;
                for (var i = 0; i <= 8; i++)
                {
                    var a = -Math.PI / 2 + i * Math.PI / 8;
                    pts.Add(new Point(x + w - radius + radius * Math.Cos(a), cy + radius * Math.Sin(a)));
                }

                for (var i = 0; i <= 8; i++)
                {
                    var a = Math.PI / 2 + i * Math.PI / 8;
                    pts.Add(new Point(x + radius + radius * Math.Cos(a), cy + radius * Math.Sin(a)));
                }

                return pts;
            case NodeShape.ForkBar:
                return [new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h)];
            default:
                return [new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h)];
        }
    }

    /// <summary>Where the segment from the shape's center toward <paramref name="toward"/> crosses the outline.</summary>
    public static Point Intersect(List<Point> outline, Point center, Point toward)
    {
        var best = toward;
        var bestT = double.PositiveInfinity;
        var (dx, dy) = (toward.X - center.X, toward.Y - center.Y);
        for (var i = 0; i < outline.Count; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Count];
            var (ex, ey) = (b.X - a.X, b.Y - a.Y);
            var denominator = dx * ey - dy * ex;
            if (Math.Abs(denominator) < 1e-12)
            {
                continue;
            }

            var t = ((a.X - center.X) * ey - (a.Y - center.Y) * ex) / denominator;
            var u = ((a.X - center.X) * dy - (a.Y - center.Y) * dx) / denominator;
            if (t >= 0 && u >= -1e-9 && u <= 1 + 1e-9 && t < bestT)
            {
                bestT = t;
                best = new Point(center.X + dx * t, center.Y + dy * t);
            }
        }

        return bestT <= 1 + 1e-9 ? best : toward;
    }

    public static bool Inside(List<Point> outline, Point p)
    {
        var inside = false;
        for (int i = 0, j = outline.Count - 1; i < outline.Count; j = i++)
        {
            if ((outline[i].Y > p.Y) != (outline[j].Y > p.Y) &&
                p.X < (outline[j].X - outline[i].X) * (p.Y - outline[i].Y) / (outline[j].Y - outline[i].Y) + outline[i].X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    public static void Draw(Canvas canvas, NodeShape shape, Rect r, string fill, string stroke, double strokeWidth, IReadOnlyList<double>? dash = null)
    {
        var (x, y, w, h) = (r.X, r.Y, r.Width, r.Height);
        var (cx, cy) = (r.CenterX, r.CenterY);
        void Fill(PathGeometry g) => canvas.Shape(g, fill, stroke, strokeWidth, dash);
        void Stroke(PathGeometry g) => canvas.Shape(g, null, stroke, strokeWidth, dash);
        switch (shape)
        {
            case NodeShape.Rect:
                Fill(PathGeometry.Rectangle(x, y, w, h));
                break;
            case NodeShape.Rounded:
                Fill(PathGeometry.Rectangle(x, y, w, h, 5));
                break;
            case NodeShape.Stadium:
                Fill(PathGeometry.Rectangle(x, y, w, h, h / 2));
                break;
            case NodeShape.Subroutine:
                Fill(PathGeometry.Rectangle(x, y, w, h));
                Stroke(PathGeometry.Line(x + 8, y, x + 8, y + h));
                Stroke(PathGeometry.Line(x + w - 8, y, x + w - 8, y + h));
                break;
            case NodeShape.Cylinder or NodeShape.LinedCylinder:
            {
                var ry = CylinderCap(w);
                var body = new PathGeometry().MoveTo(x, y + ry);
                body.ArcTo(cx, y + ry, w / 2, ry, Math.PI, -Math.PI);
                body.LineTo(x + w, y + h - ry);
                body.ArcTo(cx, y + h - ry, w / 2, ry, 0, Math.PI);
                body.Close();
                Fill(body);
                var front = new PathGeometry().MoveTo(x, y + ry);
                front.ArcTo(cx, y + ry, w / 2, ry, Math.PI, Math.PI);
                Stroke(front);
                if (shape == NodeShape.LinedCylinder)
                {
                    var line = new PathGeometry().MoveTo(x, y + 3 * ry);
                    line.ArcTo(cx, y + 3 * ry, w / 2, ry, Math.PI, -Math.PI);
                    Stroke(line);
                }

                break;
            }

            case NodeShape.HorizontalCylinder:
            {
                var rx = CylinderCap(h);
                var body = new PathGeometry().MoveTo(x + rx, y);
                body.LineTo(x + w - rx, y);
                body.ArcTo(x + w - rx, cy, rx, h / 2, -Math.PI / 2, Math.PI);
                body.LineTo(x + rx, y + h);
                body.ArcTo(x + rx, cy, rx, h / 2, Math.PI / 2, Math.PI);
                body.Close();
                Fill(body);
                var front = new PathGeometry().MoveTo(x + w - rx, y);
                front.ArcTo(x + w - rx, cy, rx, h / 2, -Math.PI / 2, -Math.PI);
                Stroke(front);
                break;
            }

            case NodeShape.Circle or NodeShape.SmallCircle:
                Fill(PathGeometry.Ellipse(cx, cy, w / 2, h / 2));
                break;
            case NodeShape.Ellipse:
                Fill(PathGeometry.Ellipse(cx, cy, w / 2, h / 2));
                break;
            case NodeShape.DoubleCircle:
                Fill(PathGeometry.Ellipse(cx, cy, w / 2, h / 2));
                Stroke(PathGeometry.Ellipse(cx, cy, w / 2 - 5, h / 2 - 5));
                break;
            case NodeShape.FilledCircle:
                canvas.Shape(PathGeometry.Ellipse(cx, cy, w / 2, h / 2), stroke, stroke, strokeWidth);
                break;
            case NodeShape.FramedCircle:
                Fill(PathGeometry.Ellipse(cx, cy, w / 2, h / 2));
                canvas.Shape(PathGeometry.Ellipse(cx, cy, w / 2 - 4, h / 2 - 4), stroke, null, 0);
                break;
            case NodeShape.CrossedCircle:
                Fill(PathGeometry.Ellipse(cx, cy, w / 2, h / 2));
                var k = Math.Sqrt(0.5);
                Stroke(PathGeometry.Line(cx - w / 2 * k, cy - h / 2 * k, cx + w / 2 * k, cy + h / 2 * k));
                Stroke(PathGeometry.Line(cx + w / 2 * k, cy - h / 2 * k, cx - w / 2 * k, cy + h / 2 * k));
                break;
            case NodeShape.ForkBar:
                canvas.Shape(PathGeometry.Rectangle(x, y, w, h, 2), stroke, stroke, strokeWidth);
                break;
            case NodeShape.Document or NodeShape.MultiDocument:
            {
                var offset = shape == NodeShape.MultiDocument ? 10.0 : 0;
                for (var layer = (int)(offset / 5); layer >= 0; layer--)
                {
                    var lx = x + layer * 5;
                    var ly = y + offset - layer * 5;
                    var lw = w - offset;
                    var lh = h - offset;
                    var wave = lh / 8;
                    var doc = new PathGeometry().MoveTo(lx, ly).LineTo(lx + lw, ly).LineTo(lx + lw, ly + lh - wave);
                    doc.CubicTo(lx + lw * 0.75, ly + lh - 2.5 * wave, lx + lw * 0.25, ly + lh + 1.5 * wave, lx, ly + lh - wave);
                    doc.Close();
                    Fill(doc);
                }

                break;
            }

            case NodeShape.Card:
                Fill(PathGeometry.Polygon([(x + 12, y), (x + w, y), (x + w, y + h), (x, y + h), (x, y + 12)]));
                break;
            case NodeShape.Delay:
            {
                var d = new PathGeometry().MoveTo(x, y).LineTo(x + w - h / 2, y);
                d.ArcTo(x + w - h / 2, cy, h / 2, h / 2, -Math.PI / 2, Math.PI);
                d.LineTo(x, y + h).Close();
                Fill(d);
                break;
            }

            case NodeShape.CurvedTrapezoid:
            {
                var d = new PathGeometry().MoveTo(x + h / 4, y).LineTo(x + w - h / 2, y);
                d.ArcTo(x + w - h / 2, cy, h / 2, h / 2, -Math.PI / 2, Math.PI);
                d.LineTo(x + h / 4, y + h).LineTo(x, cy).Close();
                Fill(d);
                break;
            }

            case NodeShape.BowTieRect:
            {
                var d = new PathGeometry().MoveTo(x + h / 4, y).LineTo(x + w - h / 4, y);
                d.ArcTo(x + w - h / 4, cy, h / 4, h / 2, -Math.PI / 2, Math.PI);
                d.LineTo(x + h / 4, y + h);
                d.ArcTo(x + h / 4, cy, h / 4, h / 2, Math.PI / 2, -Math.PI);
                d.Close();
                Fill(d);
                break;
            }

            case NodeShape.Flag:
            {
                var wave = h / 8;
                var d = new PathGeometry().MoveTo(x, y + wave);
                d.CubicTo(x + w / 4, y - wave, x + w / 4, y - wave, cx, y + wave);
                d.CubicTo(x + 3 * w / 4, y + 3 * wave, x + 3 * w / 4, y + 3 * wave, x + w, y + wave);
                d.LineTo(x + w, y + h - wave);
                d.CubicTo(x + 3 * w / 4, y + h + wave, x + 3 * w / 4, y + h + wave, cx, y + h - wave);
                d.CubicTo(x + w / 4, y + h - 3 * wave, x + w / 4, y + h - 3 * wave, x, y + h - wave);
                d.Close();
                Fill(d);
                break;
            }

            case NodeShape.Hourglass:
                Fill(PathGeometry.Polygon([(x, y), (x + w, y), (x, y + h), (x + w, y + h)]));
                break;
            case NodeShape.DividedRect:
                Fill(PathGeometry.Rectangle(x, y, w, h));
                Stroke(PathGeometry.Line(x, y + 10, x + w, y + 10));
                break;
            case NodeShape.WindowPane:
                Fill(PathGeometry.Rectangle(x, y, w, h));
                Stroke(PathGeometry.Line(x, y + 10, x + w, y + 10));
                Stroke(PathGeometry.Line(x + 10, y, x + 10, y + h));
                break;
            case NodeShape.StackedRect:
                Fill(PathGeometry.Rectangle(x + 10, y, w - 10, h - 10));
                Fill(PathGeometry.Rectangle(x + 5, y + 5, w - 10, h - 10));
                Fill(PathGeometry.Rectangle(x, y + 10, w - 10, h - 10));
                break;
            case NodeShape.TaggedRect:
                Fill(PathGeometry.Rectangle(x, y, w, h));
                canvas.Shape(PathGeometry.Polygon([(x + w - 12, y + h), (x + w, y + h), (x + w, y + h - 12)]), stroke, null, 0);
                break;
            case NodeShape.LinedRect:
                Fill(PathGeometry.Rectangle(x, y, w, h));
                Stroke(PathGeometry.Line(x + 8, y, x + 8, y + h));
                break;
            case NodeShape.NotchedPentagon:
                Fill(PathGeometry.Polygon([(x + 10, y), (x + w - 10, y), (x + w, y + 10), (x + w, y + h), (x, y + h), (x, y + 10)]));
                break;
            case NodeShape.Bolt:
                Fill(PathGeometry.Polygon([(x + w * 0.6, y), (x + w * 0.2, y + h * 0.55), (x + w * 0.5, y + h * 0.55), (x + w * 0.4, y + h), (x + w * 0.8, y + h * 0.45), (x + w * 0.5, y + h * 0.45)]));
                break;
            case NodeShape.TextOnly:
                break;
            case NodeShape.BraceLeft or NodeShape.BraceRight or NodeShape.Braces:
                if (shape != NodeShape.BraceRight)
                {
                    Stroke(new PathGeometry().MoveTo(x + 10, y).CubicTo(x, y, x + 5, cy, x, cy).CubicTo(x + 5, cy, x, y + h, x + 10, y + h));
                }

                if (shape != NodeShape.BraceLeft)
                {
                    Stroke(new PathGeometry().MoveTo(x + w - 10, y).CubicTo(x + w, y, x + w - 5, cy, x + w, cy).CubicTo(x + w - 5, cy, x + w, y + h, x + w - 10, y + h));
                }

                break;
            default:
                Fill(PathGeometry.Polygon(Outline(shape, r).Select(p => (p.X, p.Y))));
                break;
        }
    }
}
