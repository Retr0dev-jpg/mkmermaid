namespace MkMermaid;

/// <summary>Stable error categories, suitable for localized messages in host applications.</summary>
public enum MermaidErrorCode
{
    Empty,
    UnknownDiagram,
    Syntax,
    UnknownReference,
    InvalidValue,
    UnclosedBlock,
    TooLong,
    TooComplex,
    TooDeep,

    /// <summary>A defect in MkMermaid itself; the message never contains source text.</summary>
    Internal
}

/// <summary>A parse or layout error. <see cref="Line"/> is 1-based in the original source, or 0 when not tied to a line.</summary>
public sealed record MermaidError(MermaidErrorCode Code, string Message, int Line);

internal sealed class MermaidParseException(MermaidErrorCode code, string message, int line) : Exception(message)
{
    public MermaidErrorCode Code { get; } = code;

    public int Line { get; } = line;

    public MermaidError ToError() => new(Code, Message, Line);
}
