using MkMermaid.Layout;
using MkMermaid.Scene;

namespace MkMermaid.Rendering;

internal enum Marker
{
    None,
    Arrow,
    OpenArrow,
    Cross,
    Circle,
    Triangle,
    FilledDiamond,
    HollowDiamond,
    Lollipop,
    One,
    ZeroOrOne,
    OneOrMore,
    ZeroOrMore,
    Contains
}

internal static class Markers
{
    /// <summary>How far the line stops short of the tip so it does not poke through the marker.</summary>
    public static double Inset(Marker marker, double scale = 1) => scale * marker switch
    {
        Marker.Arrow => 8,
        Marker.OpenArrow => 1,
        Marker.Cross => 0,
        Marker.Circle => 10,
        Marker.Triangle => 16,
        Marker.FilledDiamond or Marker.HollowDiamond => 16,
        Marker.Lollipop => 12,
        Marker.Contains => 18,
        _ => 0
    };

    /// <summary>
    /// Draws a marker whose tip is at <paramref name="tip"/>; (<paramref name="dx"/>, <paramref name="dy"/>) is the unit
    /// direction of travel toward the tip.
    /// </summary>
    public static void Draw(Canvas canvas, Marker marker, Point tip, double dx, double dy, string stroke, string background, double strokeWidth = 1, double scale = 1)
    {
        // Local frame: u along the line toward the tip, v perpendicular.
        (double X, double Y) P(double u, double v) => (tip.X + dx * u - dy * v, tip.Y + dy * u + dx * v);
        var s = scale;
        switch (marker)
        {
            case Marker.Arrow:
                canvas.Shape(PathGeometry.Polygon([P(0, 0), P(-10 * s, 5 * s), P(-10 * s, -5 * s)]), stroke, stroke, strokeWidth);
                break;
            case Marker.OpenArrow:
                canvas.Shape(PathGeometry.Polyline([P(-9 * s, 6 * s), P(0, 0), P(-9 * s, -6 * s)]), null, stroke, Math.Max(1, strokeWidth));
                break;
            case Marker.Cross:
                canvas.Shape(new PathGeometry().MoveTo(P(-9 * s, -5 * s).X, P(-9 * s, -5 * s).Y).LineTo(P(-1 * s, 5 * s).X, P(-1 * s, 5 * s).Y)
                    .MoveTo(P(-9 * s, 5 * s).X, P(-9 * s, 5 * s).Y).LineTo(P(-1 * s, -5 * s).X, P(-1 * s, -5 * s).Y), null, stroke, 2 * Math.Max(1, strokeWidth));
                break;
            case Marker.Circle:
                var c = P(-5 * s, 0);
                canvas.Circle(c.X, c.Y, 5 * s, stroke, stroke, strokeWidth);
                break;
            case Marker.Triangle:
                canvas.Shape(PathGeometry.Polygon([P(0, 0), P(-16 * s, 7 * s), P(-16 * s, -7 * s)]), background, stroke, Math.Max(1, strokeWidth));
                break;
            case Marker.FilledDiamond or Marker.HollowDiamond:
                canvas.Shape(PathGeometry.Polygon([P(0, 0), P(-8 * s, 6 * s), P(-16 * s, 0), P(-8 * s, -6 * s)]),
                    marker == Marker.FilledDiamond ? stroke : background, stroke, Math.Max(1, strokeWidth));
                break;
            case Marker.Lollipop:
                var l = P(-6 * s, 0);
                canvas.Circle(l.X, l.Y, 6 * s, background, stroke, Math.Max(1, strokeWidth));
                break;
            case Marker.Contains:
                var o = P(-9 * s, 0);
                canvas.Circle(o.X, o.Y, 9 * s, background, stroke, Math.Max(1, strokeWidth));
                var (a1, a2, b1, b2) = (P(-18 * s, 0), P(0, 0), P(-9 * s, -9 * s), P(-9 * s, 9 * s));
                canvas.Line(a1.X, a1.Y, a2.X, a2.Y, stroke, Math.Max(1, strokeWidth));
                canvas.Line(b1.X, b1.Y, b2.X, b2.Y, stroke, Math.Max(1, strokeWidth));
                break;
            case Marker.One:
                Bar(-8);
                Bar(-14);
                break;
            case Marker.ZeroOrOne:
                Bar(-8);
                Ring(-20);
                break;
            case Marker.OneOrMore:
                Crow();
                Bar(-20);
                break;
            case Marker.ZeroOrMore:
                Crow();
                Ring(-24);
                break;
        }

        void Bar(double u)
        {
            var a = P(u * s, -7 * s);
            var b = P(u * s, 7 * s);
            canvas.Line(a.X, a.Y, b.X, b.Y, stroke, Math.Max(1, strokeWidth));
        }

        void Ring(double u)
        {
            var center = P(u * s, 0);
            canvas.Circle(center.X, center.Y, 5 * s, background, stroke, Math.Max(1, strokeWidth));
        }

        void Crow()
        {
            var root = P(-14 * s, 0);
            var top = P(0, -8 * s);
            var bottom = P(0, 8 * s);
            canvas.Shape(new PathGeometry().MoveTo(top.X, top.Y).LineTo(root.X, root.Y).LineTo(bottom.X, bottom.Y), null, stroke, Math.Max(1, strokeWidth));
        }
    }
}
