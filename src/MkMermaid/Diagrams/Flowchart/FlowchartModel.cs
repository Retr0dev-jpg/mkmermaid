using MkMermaid.Layout;
using MkMermaid.Rendering;

namespace MkMermaid.Diagrams.Flowchart;

internal enum LineKind
{
    Normal,
    Dotted,
    Thick,
    Invisible
}

internal sealed class FlowNode(string id, int order)
{
    public string Id { get; } = id;

    public int Order { get; } = order;

    public string? Label { get; set; }

    public NodeShape Shape { get; set; } = NodeShape.Rect;

    public bool Explicit { get; set; }

    public string? Subgraph { get; set; }

    public List<string> Classes { get; } = [];

    public Style Style { get; set; } = Style.None;
}

internal sealed class FlowEdge
{
    public required string From { get; init; }

    public required string To { get; init; }

    public string? Id { get; init; }

    public string? Label { get; init; }

    public LineKind Line { get; init; }

    public Marker Start { get; init; }

    public Marker End { get; init; }

    public int Length { get; init; } = 1;

    public Style Style { get; set; } = Style.None;

    public string? Curve { get; set; }
}

internal sealed class FlowSubgraph(string id, string title, string? parent, int order)
{
    public string Id { get; } = id;

    public string Title { get; } = title;

    public string? Parent { get; } = parent;

    public int Order { get; } = order;

    public LayoutDirection? Direction { get; set; }

    public List<string> Classes { get; } = [];

    public Style Style { get; set; } = Style.None;
}

internal sealed class FlowchartModel : IDiagramModel
{
    public string? Title => null;

    public Scene.DiagramScene Render(RenderContext context, DiagramInfo info) => FlowchartRenderer.Render(this, context, info);

    public LayoutDirection Direction { get; set; } = LayoutDirection.TopToBottom;

    public Dictionary<string, FlowNode> Nodes { get; } = new(StringComparer.Ordinal);

    public List<FlowEdge> Edges { get; } = [];

    public Dictionary<string, FlowSubgraph> Subgraphs { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, Style> ClassDefs { get; } = new(StringComparer.Ordinal);

    public Style DefaultLinkStyle { get; set; } = Style.None;

    public string? DefaultCurve { get; set; }

    public static LayoutDirection? ParseDirection(string text) => text.Trim().ToUpperInvariant() switch
    {
        "TB" or "TD" or "V" => LayoutDirection.TopToBottom,
        "BT" or "^" => LayoutDirection.BottomToTop,
        "LR" or ">" => LayoutDirection.LeftToRight,
        "RL" or "<" => LayoutDirection.RightToLeft,
        _ => null
    };
}
