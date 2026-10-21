using MkMermaid.Layout;
using MkMermaid.Scene;

namespace MkMermaid.Rendering;

internal enum CurveKind
{
    Basis,
    Linear,
    Cardinal,
    Step,
    StepBefore,
    StepAfter,
    BumpX,
    BumpY
}

internal static class Curves
{
    public static CurveKind Parse(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "linear" => CurveKind.Linear,
        "cardinal" or "catmullrom" or "natural" or "monotonex" or "monotoney" => CurveKind.Cardinal,
        "step" => CurveKind.Step,
        "stepbefore" => CurveKind.StepBefore,
        "stepafter" => CurveKind.StepAfter,
        "bumpx" => CurveKind.BumpX,
        "bumpy" => CurveKind.BumpY,
        _ => CurveKind.Basis
    };

    public static PathGeometry Build(IReadOnlyList<Point> points, CurveKind kind)
    {
        var path = new PathGeometry();
        if (points.Count == 0)
        {
            return path;
        }

        path.MoveTo(points[0].X, points[0].Y);
        if (points.Count == 1)
        {
            return path;
        }

        switch (kind)
        {
            case CurveKind.Linear:
                for (var i = 1; i < points.Count; i++)
                {
                    path.LineTo(points[i].X, points[i].Y);
                }

                break;
            case CurveKind.Step or CurveKind.StepBefore or CurveKind.StepAfter:
                for (var i = 1; i < points.Count; i++)
                {
                    var (a, b) = (points[i - 1], points[i]);
                    switch (kind)
                    {
                        case CurveKind.StepBefore:
                            path.LineTo(a.X, b.Y).LineTo(b.X, b.Y);
                            break;
                        case CurveKind.StepAfter:
                            path.LineTo(b.X, a.Y).LineTo(b.X, b.Y);
                            break;
                        default:
                            var mid = (a.X + b.X) / 2;
                            path.LineTo(mid, a.Y).LineTo(mid, b.Y).LineTo(b.X, b.Y);
                            break;
                    }
                }

                break;
            case CurveKind.BumpX or CurveKind.BumpY:
                for (var i = 1; i < points.Count; i++)
                {
                    var (a, b) = (points[i - 1], points[i]);
                    if (kind == CurveKind.BumpX)
                    {
                        var mx = (a.X + b.X) / 2;
                        path.CubicTo(mx, a.Y, mx, b.Y, b.X, b.Y);
                    }
                    else
                    {
                        var my = (a.Y + b.Y) / 2;
                        path.CubicTo(a.X, my, b.X, my, b.X, b.Y);
                    }
                }

                break;
            case CurveKind.Cardinal:
                for (var i = 1; i < points.Count; i++)
                {
                    var p0 = points[Math.Max(0, i - 2)];
                    var p1 = points[i - 1];
                    var p2 = points[i];
                    var p3 = points[Math.Min(points.Count - 1, i + 1)];
                    path.CubicTo(
                        p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6,
                        p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6,
                        p2.X, p2.Y);
                }

                break;
            default:
                Basis(path, points);
                break;
        }

        return path;
    }

    /// <summary>d3's curveBasis: a uniform cubic B-spline that starts and ends at the first and last points.</summary>
    private static void Basis(PathGeometry path, IReadOnlyList<Point> points)
    {
        if (points.Count == 2)
        {
            path.LineTo(points[1].X, points[1].Y);
            return;
        }

        var (x0, y0) = (points[0].X, points[0].Y);
        var (x1, y1) = (points[1].X, points[1].Y);
        path.LineTo((5 * x0 + x1) / 6, (5 * y0 + y1) / 6);
        for (var i = 2; i < points.Count; i++)
        {
            var (x, y) = (points[i].X, points[i].Y);
            path.CubicTo((2 * x0 + x1) / 3, (2 * y0 + y1) / 3, (x0 + 2 * x1) / 3, (y0 + 2 * y1) / 3, (x0 + 4 * x1 + x) / 6, (y0 + 4 * y1 + y) / 6);
            (x0, y0, x1, y1) = (x1, y1, x, y);
        }

        path.CubicTo((2 * x0 + x1) / 3, (2 * y0 + y1) / 3, (x0 + 2 * x1) / 3, (y0 + 2 * y1) / 3, x1, y1);
    }

    /// <summary>Moves the last point back along the final segment, leaving room for a marker.</summary>
    public static List<Point> ShortenEnd(List<Point> points, double amount)
    {
        if (points.Count < 2 || amount <= 0)
        {
            return points;
        }

        var end = points[^1];
        var previous = points[^2];
        var length = Distance(previous, end);
        if (length < 1e-6)
        {
            return points;
        }

        var cut = Math.Min(amount, length * 0.9);
        points[^1] = new Point(end.X - (end.X - previous.X) / length * cut, end.Y - (end.Y - previous.Y) / length * cut);
        return points;
    }

    public static List<Point> ShortenStart(List<Point> points, double amount)
    {
        points.Reverse();
        ShortenEnd(points, amount);
        points.Reverse();
        return points;
    }

    public static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>Unit direction from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static (double X, double Y) Direction(Point from, Point to)
    {
        var d = Distance(from, to);
        return d < 1e-9 ? (0, 1) : ((to.X - from.X) / d, (to.Y - from.Y) / d);
    }

    /// <summary>Point at the middle of a polyline, measured along its length.</summary>
    public static Point Midpoint(IReadOnlyList<Point> points)
    {
        if (points.Count == 0)
        {
            return default;
        }

        var total = 0.0;
        for (var i = 1; i < points.Count; i++)
        {
            total += Distance(points[i - 1], points[i]);
        }

        var half = total / 2;
        for (var i = 1; i < points.Count; i++)
        {
            var d = Distance(points[i - 1], points[i]);
            if (half <= d && d > 0)
            {
                var t = half / d;
                return new Point(points[i - 1].X + (points[i].X - points[i - 1].X) * t, points[i - 1].Y + (points[i].Y - points[i - 1].Y) * t);
            }

            half -= d;
        }

        return points[^1];
    }
}
