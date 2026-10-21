using MkMermaid.Rendering;
using MkMermaid.Scene;

namespace MkMermaid.Diagrams.Sequence;

internal enum ParticipantKind
{
    Participant,
    Actor,
    Boundary,
    Control,
    Entity,
    Database,
    Collections,
    Queue
}

internal sealed class Participant(string id, int order)
{
    public string Id { get; } = id;

    public int Order { get; } = order;

    public string Label { get; set; } = id;

    public ParticipantKind Kind { get; set; }

    public int Box { get; set; } = -1;

    /// <summary>Created by a <c>create</c> statement: drawn where it is first addressed.</summary>
    public bool Created { get; set; }

    public bool Destroyed { get; set; }
}

internal sealed record SequenceBox(string? Label, string? Color);

internal enum NotePlacement
{
    LeftOf,
    RightOf,
    Over
}

internal enum BlockKind
{
    Loop,
    Alt,
    Opt,
    Par,
    Critical,
    Break,
    Rect
}

internal abstract record SequenceEvent(int Line);

internal sealed record MessageEvent(
    int Line, string From, string To, bool Dotted, Marker Head, bool Both, string Text, bool ActivateTarget, bool DeactivateSource)
    : SequenceEvent(Line);

internal sealed record NoteEvent(int Line, NotePlacement Placement, string First, string? Second, string Text) : SequenceEvent(Line);

internal sealed record ActivationEvent(int Line, string Actor, bool On) : SequenceEvent(Line);

internal sealed record BlockStartEvent(int Line, BlockKind Kind, string Label) : SequenceEvent(Line);

internal sealed record BlockDividerEvent(int Line, string Label) : SequenceEvent(Line);

internal sealed record BlockEndEvent(int Line) : SequenceEvent(Line);

internal sealed record CreateEvent(int Line, string Actor) : SequenceEvent(Line);

internal sealed record DestroyEvent(int Line, string Actor) : SequenceEvent(Line);

internal sealed record AutonumberEvent(int Line, int Start, int Step, bool Off) : SequenceEvent(Line);

internal sealed class SequenceModel : IDiagramModel
{
    public string? Title { get; set; }

    public Dictionary<string, Participant> Participants { get; } = new(StringComparer.Ordinal);

    public List<SequenceBox> Boxes { get; } = [];

    public List<SequenceEvent> Events { get; } = [];

    public Participant Ensure(string id)
    {
        if (!Participants.TryGetValue(id, out var participant))
        {
            participant = new Participant(id, Participants.Count);
            Participants[id] = participant;
        }

        return participant;
    }

    public DiagramScene Render(RenderContext context, DiagramInfo info) => SequenceRenderer.Render(this, context, info);
}
