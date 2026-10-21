using MkMermaid.Config;
using MkMermaid.Rendering;
using MkMermaid.Scene;
using MkMermaid.Text;

namespace MkMermaid;

internal interface IDiagramModel
{
    string? Title { get; }

    DiagramScene Render(RenderContext context, DiagramInfo info);
}

internal sealed record DiagramInfo(DiagramKind Kind, string? Title, string? AccTitle, string? AccDescription, MermaidConfig Config);

/// <summary>
/// A parsed diagram. Parsing does not measure text, so it is cheap and thread-agnostic; call
/// <see cref="MermaidEngine.Layout(MermaidDiagram, MermaidOptions?)"/> to produce a drawable scene.
/// </summary>
public sealed class MermaidDiagram
{
    internal MermaidDiagram(DiagramKind kind, IDiagramModel? model, MermaidConfig config, string? title, string? accTitle, string? accDescription, MermaidError? error)
    {
        Kind = kind;
        Model = model;
        Config = config;
        Title = title;
        AccessibleTitle = accTitle;
        AccessibleDescription = accDescription;
        Error = error;
    }

    public DiagramKind Kind { get; }

    /// <summary>Title from the front matter or the diagram's own <c>title</c> statement.</summary>
    public string? Title { get; }

    public string? AccessibleTitle { get; }

    public string? AccessibleDescription { get; }

    public MermaidConfig Config { get; }

    /// <summary>Set when the source could not be parsed; the diagram then lays out as an error box.</summary>
    public MermaidError? Error { get; }

    internal IDiagramModel? Model { get; }
}

/// <summary>Layout options supplied by the host.</summary>
public sealed class MermaidOptions
{
    /// <summary>Theme used when the diagram does not request one: default, base, dark, forest or neutral.</summary>
    public string Theme { get; init; } = "default";

    /// <summary>Ignore the theme and theme variables requested by the diagram itself.</summary>
    public bool ForceTheme { get; init; }

    /// <summary>Theme variables applied under the diagram's own <c>themeVariables</c>.</summary>
    public IReadOnlyDictionary<string, string>? ThemeVariables { get; init; }

    /// <summary>Base font size in pixels, used unless the diagram sets <c>fontSize</c>.</summary>
    public double FontSize { get; init; } = 16;

    public ITextMeasurer TextMeasurer { get; init; } = DefaultTextMeasurer.Instance;

    /// <summary>Paint the theme background behind the diagram (otherwise transparent).</summary>
    public bool PaintBackground { get; init; }
}
