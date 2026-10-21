using MkMermaid.Layout;
using MkMermaid.Rendering;
using MkMermaid.Scene;

namespace MkMermaid.Diagrams.Class;

internal sealed record ClassMember(string Text, bool IsMethod, bool Static, bool Abstract);

internal sealed class ClassNode(string id, int order)
{
    public string Id { get; } = id;

    public int Order { get; } = order;

    public string? Label { get; set; }

    public string? Generic { get; set; }

    public List<string> Annotations { get; } = [];

    public List<ClassMember> Attributes { get; } = [];

    public List<ClassMember> Methods { get; } = [];

    public string? Namespace { get; set; }

    public List<string> Classes { get; } = [];

    public Style? Style { get; set; }
}

internal sealed record ClassRelation(
    string From, string To, Marker Start, Marker End, bool Dashed, string? Label, string? FromCardinality, string? ToCardinality);

internal sealed record ClassNote(string Text, string? For);

internal sealed class ClassModel : IDiagramModel
{
    public string? Title => null;

    public LayoutDirection Direction { get; set; } = LayoutDirection.TopToBottom;

    public Dictionary<string, ClassNode> Classes { get; } = new(StringComparer.Ordinal);

    /// <summary>Namespace name to parent namespace (null at the top level).</summary>
    public Dictionary<string, string?> Namespaces { get; } = new(StringComparer.Ordinal);

    public List<ClassRelation> Relations { get; } = [];

    public List<ClassNote> Notes { get; } = [];

    public Dictionary<string, Style> ClassDefs { get; } = new(StringComparer.Ordinal);

    public ClassNode Ensure(string id)
    {
        if (!Classes.TryGetValue(id, out var node))
        {
            node = new ClassNode(id, Classes.Count);
            Classes[id] = node;
        }

        return node;
    }

    public DiagramScene Render(RenderContext context, DiagramInfo info) => ClassRenderer.Render(this, context, info);
}
