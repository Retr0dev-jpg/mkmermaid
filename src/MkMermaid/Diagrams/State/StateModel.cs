using MkMermaid.Layout;
using MkMermaid.Rendering;
using MkMermaid.Scene;

namespace MkMermaid.Diagrams.State;

internal enum StateType
{
    Normal,
    Start,
    End,
    Fork,
    Join,
    Choice
}

internal sealed record StateNote(bool Left, string Text);

internal sealed class StateNode(string id, int order, string? scope)
{
    public string Id { get; } = id;

    public int Order { get; } = order;

    /// <summary>Cluster that contains the state (a composite state or one of its regions), null at the top level.</summary>
    public string? Scope { get; set; } = scope;

    public string? Label { get; set; }

    public List<string> Descriptions { get; } = [];

    public StateType Type { get; set; }

    public List<string> Classes { get; } = [];

    public Style? Style { get; set; }

    public StateNote? Note { get; set; }

    /// <summary>Clusters for the concurrent regions of a composite state (empty when it has a single region).</summary>
    public List<string> Regions { get; } = [];

    public bool Composite { get; set; }
}

internal sealed record StateTransition(string From, string To, string? Label);

internal sealed class StateModel : IDiagramModel
{
    public string? Title => null;

    public LayoutDirection Direction { get; set; } = LayoutDirection.TopToBottom;

    public Dictionary<string, StateNode> States { get; } = new(StringComparer.Ordinal);

    /// <summary>Parent of each region cluster (the composite state).</summary>
    public Dictionary<string, string> RegionParents { get; } = new(StringComparer.Ordinal);

    public List<StateTransition> Transitions { get; } = [];

    public Dictionary<string, Style> ClassDefs { get; } = new(StringComparer.Ordinal);

    public StateNode Ensure(string id, string? scope)
    {
        if (!States.TryGetValue(id, out var node))
        {
            node = new StateNode(id, States.Count, scope);
            States[id] = node;
        }

        return node;
    }

    public DiagramScene Render(RenderContext context, DiagramInfo info) => StateRenderer.Render(this, context, info);
}
