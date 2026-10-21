using MkMermaid.Layout;
using MkMermaid.Scene;
using MkMermaid.Text;

namespace MkMermaid.Rendering;

internal sealed record EdgeVisual(
    string Stroke,
    double Width,
    IReadOnlyList<double>? Dash,
    Marker Start,
    Marker End,
    CurveKind Curve,
    MeasuredText? Label = null,
    string? LabelColor = null,
    string? LabelBackground = null,
    MeasuredText? StartLabel = null,
    MeasuredText? EndLabel = null,
    bool Visible = true,
    double MarkerScale = 1);

internal static class EdgeDrawer
{
    /// <summary>Clips a route that starts and ends at shape centers to the shapes' outlines.</summary>
    public static List<Point> Clip(IReadOnlyList<Point> route, List<Point>? fromOutline, List<Point>? toOutline)
    {
        var points = route.ToList();
        if (points.Count < 2)
        {
            return points;
        }

        if (fromOutline is not null && Shapes.Inside(fromOutline, points[0]))
        {
            while (points.Count > 2 && Shapes.Inside(fromOutline, points[1]))
            {
                points.RemoveAt(1);
            }

            points[0] = Shapes.Intersect(fromOutline, points[0], points[1]);
        }

        if (toOutline is not null && Shapes.Inside(toOutline, points[^1]))
        {
            while (points.Count > 2 && Shapes.Inside(toOutline, points[^2]))
            {
                points.RemoveAt(points.Count - 2);
            }

            points[^1] = Shapes.Intersect(toOutline, points[^1], points[^2]);
        }

        return points;
    }

    public static void Draw(Canvas canvas, List<Point> points, EdgeVisual visual, Point? labelCenter, string background)
    {
        if (points.Count < 2)
        {
            return;
        }

        var startTip = points[0];
        var endTip = points[^1];
        var endDirection = Curves.Direction(points[^2], points[^1]);
        var startDirection = Curves.Direction(points[1], points[0]);
        if (visual.Visible)
        {
            var route = points.ToList();
            Curves.ShortenEnd(route, Markers.Inset(visual.End, visual.MarkerScale));
            Curves.ShortenStart(route, Markers.Inset(visual.Start, visual.MarkerScale));
            canvas.Shape(Curves.Build(route, visual.Curve), null, visual.Stroke, visual.Width, visual.Dash);
            Markers.Draw(canvas, visual.End, endTip, endDirection.X, endDirection.Y, visual.Stroke, background, visual.Width, visual.MarkerScale);
            Markers.Draw(canvas, visual.Start, startTip, startDirection.X, startDirection.Y, visual.Stroke, background, visual.Width, visual.MarkerScale);
        }

        var color = visual.LabelColor ?? visual.Stroke;
        if (visual.Label is { Lines.Count: > 0 } label)
        {
            var center = labelCenter ?? Curves.Midpoint(points);
            if (visual.LabelBackground is { } fill)
            {
                canvas.Rect(center.X - label.Width / 2 - 2, center.Y - label.Height / 2, label.Width + 4, label.Height, fill, null, 0);
            }

            canvas.LabelCentered(label, center.X, center.Y, color);
        }

        if (visual.StartLabel is { Lines.Count: > 0 } startLabel)
        {
            EndLabel(canvas, startLabel, startTip, (-startDirection.X, -startDirection.Y), color);
        }

        if (visual.EndLabel is { Lines.Count: > 0 } endLabel)
        {
            EndLabel(canvas, endLabel, endTip, (-endDirection.X, -endDirection.Y), color);
        }
    }

    /// <summary>Places a small label beside the line, a little away from the tip (cardinalities).</summary>
    private static void EndLabel(Canvas canvas, MeasuredText label, Point tip, (double X, double Y) inward, string color)
    {
        var along = 18 + label.Height / 2;
        var side = 6 + label.Width / 2;
        var cx = tip.X + inward.X * along - inward.Y * side;
        var cy = tip.Y + inward.Y * along + inward.X * side;
        canvas.LabelCentered(label, cx, cy, color);
    }
}
