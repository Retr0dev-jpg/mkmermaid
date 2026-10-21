namespace MkMermaid.Layout;

public readonly record struct Point(double X, double Y);

public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    public double CenterX => X + Width / 2;

    public double CenterY => Y + Height / 2;

    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool Contains(Point p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;
}

public enum LayoutDirection
{
    TopToBottom,
    BottomToTop,
    LeftToRight,
    RightToLeft
}

/// <summary>
/// Input for <see cref="LayeredLayout"/>: nodes with sizes, optional nested clusters and directed edges.
/// Edge endpoints may name nodes or clusters.
/// </summary>
public sealed class LayeredGraph
{
    internal readonly List<NodeSpec> Nodes = [];
    internal readonly Dictionary<string, int> NodeIndex = new(StringComparer.Ordinal);
    internal readonly List<ClusterSpec> Clusters = [];
    internal readonly Dictionary<string, int> ClusterIndex = new(StringComparer.Ordinal);
    internal readonly List<EdgeSpec> Edges = [];

    public LayoutDirection Direction { get; set; } = LayoutDirection.TopToBottom;

    /// <summary>Horizontal gap between neighboring nodes in a rank (vertical for left-to-right layouts).</summary>
    public double NodeSpacing { get; set => field = Spacing(value); } = 50;

    /// <summary>Gap between consecutive ranks.</summary>
    public double RankSpacing { get; set => field = Spacing(value); } = 50;

    /// <summary>Gap between edges routed through the same rank.</summary>
    public double EdgeSpacing { get; set => field = Spacing(value); } = 10;

    public int NodeCount => Nodes.Count;

    public int EdgeCount => Edges.Count;

    public void AddNode(string id, double width, double height, string? cluster = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (NodeIndex.ContainsKey(id) || ClusterIndex.ContainsKey(id))
        {
            throw new ArgumentException($"Duplicate id '{id}'.", nameof(id));
        }

        NodeIndex[id] = Nodes.Count;
        Nodes.Add(new NodeSpec(id, Spacing(width), Spacing(height), cluster));
    }

    public void AddCluster(string id, string? parent = null, double labelWidth = 0, double labelHeight = 0, double padding = 8, LayoutDirection? direction = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (NodeIndex.ContainsKey(id) || ClusterIndex.ContainsKey(id))
        {
            throw new ArgumentException($"Duplicate id '{id}'.", nameof(id));
        }

        ClusterIndex[id] = Clusters.Count;
        Clusters.Add(new ClusterSpec(id, parent, Spacing(labelWidth), Spacing(labelHeight), Spacing(padding), direction));
    }

    public bool Contains(string id) => NodeIndex.ContainsKey(id) || ClusterIndex.ContainsKey(id);

    /// <summary>Adds an edge and returns its index in <see cref="LayeredResult.Edges"/>.</summary>
    public int AddEdge(string from, string to, double labelWidth = 0, double labelHeight = 0, int minLength = 1)
    {
        if (!Contains(from) || !Contains(to))
        {
            throw new ArgumentException($"Unknown edge endpoint '{(Contains(from) ? to : from)}'.");
        }

        Edges.Add(new EdgeSpec(from, to, Spacing(labelWidth), Spacing(labelHeight), Math.Clamp(minLength, 1, 16)));
        return Edges.Count - 1;
    }

    /// <summary>Sizes and gaps are clamped to [0, 100 000]; NaN counts as 0.</summary>
    private static double Spacing(double value) => double.IsNaN(value) ? 0 : Math.Clamp(value, 0, Config.MermaidConfig.MaxNumber);

    internal sealed record NodeSpec(string Id, double Width, double Height, string? Cluster);

    internal sealed record ClusterSpec(string Id, string? Parent, double LabelWidth, double LabelHeight, double Padding, LayoutDirection? Direction);

    internal sealed record EdgeSpec(string From, string To, double LabelWidth, double LabelHeight, int MinLength);
}

/// <summary>Node box (top-left corner and size).</summary>
public sealed record LayeredNode(string Id, Rect Bounds);

/// <summary>
/// Edge route from the center of <see cref="From"/> to the center of <see cref="To"/>, through the bend points.
/// Callers clip the first and last segments to their shapes (see <see cref="LayeredLayout.Clip"/>).
/// </summary>
public sealed record LayeredEdge(int Index, string From, string To, IReadOnlyList<Point> Points, Point? LabelCenter);

public sealed record LayeredCluster(string Id, Rect Bounds, Rect LabelBounds);

public sealed record LayeredResult(
    double Width,
    double Height,
    IReadOnlyDictionary<string, LayeredNode> Nodes,
    IReadOnlyList<LayeredEdge> Edges,
    IReadOnlyDictionary<string, LayeredCluster> Clusters);
