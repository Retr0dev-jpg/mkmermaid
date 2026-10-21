using MkMermaid.Config;
using MkMermaid.Diagrams;
using MkMermaid.Parsing;
using MkMermaid.Rendering;
using MkMermaid.Scene;
using MkMermaid.Theming;

namespace MkMermaid;

/// <summary>Entry point: parse Mermaid source, lay it out and render it. Invalid input never throws.</summary>
public static class MermaidEngine
{
    public const int MaxSourceLength = 50_000;

    /// <summary>Upper bound on nodes, participants, tasks or similar elements in one diagram.</summary>
    public const int MaxElements = 2_000;

    /// <summary>Upper bound on edges, messages or links in one diagram.</summary>
    public const int MaxConnections = 4_000;

    public static IReadOnlyList<string> Themes => Theme.Names;

    public static DiagramKind Detect(string? source) => Source.Detect(source);

    public static MermaidDiagram Parse(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return Failed(DiagramKind.Unknown, new MermaidError(MermaidErrorCode.Empty, "The diagram is empty.", 0), MermaidConfig.Empty);
        }

        Source? parsed = null;
        try
        {
            parsed = Source.Preprocess(source);
            var model = Parsers.Parse(parsed);
            return new MermaidDiagram(parsed.Kind, model, parsed.Config, parsed.Title ?? model.Title, parsed.AccTitle, parsed.AccDescription, null);
        }
        catch (MermaidParseException ex)
        {
            return Failed(parsed?.Kind ?? DiagramKind.Unknown, ex.ToError(), parsed?.Config ?? MermaidConfig.Empty);
        }
        catch (Exception ex) when (Unexpected(ex))
        {
            return Failed(parsed?.Kind ?? DiagramKind.Unknown, InternalError(ex), parsed?.Config ?? MermaidConfig.Empty);
        }
    }

    public static MermaidError? Validate(string? source) => Parse(source).Error;

    public static DiagramScene Layout(MermaidDiagram diagram, MermaidOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        options ??= new MermaidOptions();
        var config = diagram.Config;
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (options.ThemeVariables is not null)
        {
            foreach (var (k, v) in options.ThemeVariables)
            {
                variables[k] = v;
            }
        }

        var themeName = options.Theme;
        if (!options.ForceTheme)
        {
            themeName = config.Theme ?? themeName;
            foreach (var (k, v) in config.ThemeVariables())
            {
                variables[k] = v;
            }
        }

        var theme = Theme.Build(themeName, variables);
        var fontSize = !options.ForceTheme && config.Get("fontSize") is not null
            ? config.GetNumber("fontSize", options.FontSize)
            : variables.ContainsKey("fontSize") ? theme.Number("fontSize", options.FontSize) : options.FontSize;
        fontSize = Math.Clamp(fontSize, 6, 72);
        var context = new RenderContext(theme, config, options.TextMeasurer, fontSize);
        var background = options.PaintBackground ? theme.Hex("background") : null;
        if (diagram.Error is not null || diagram.Model is null)
        {
            return ErrorScene(context, diagram.Kind, diagram.Error ?? new MermaidError(MermaidErrorCode.Syntax, "The diagram could not be parsed.", 0), background);
        }

        try
        {
            var info = new DiagramInfo(diagram.Kind, diagram.Title, diagram.AccessibleTitle, diagram.AccessibleDescription, config);
            var scene = diagram.Model.Render(context, info);
            return background is null ? scene : scene with { Background = background };
        }
        catch (MermaidParseException ex)
        {
            return ErrorScene(context, diagram.Kind, ex.ToError(), background);
        }
        catch (Exception ex) when (Unexpected(ex))
        {
            return ErrorScene(context, diagram.Kind, InternalError(ex), background);
        }
    }

    /// <summary>Out-of-memory and cancellation are left to the host; everything else is a library defect.</summary>
    private static bool Unexpected(Exception ex) => ex is not (OutOfMemoryException or OperationCanceledException);

    private static MermaidError InternalError(Exception ex) => ex is InsufficientExecutionStackException
        ? new MermaidError(MermaidErrorCode.TooDeep, "The diagram is nested too deeply.", 0)
        : new MermaidError(MermaidErrorCode.Internal, $"The diagram could not be rendered ({ex.GetType().Name}).", 0);

    public static DiagramScene Render(string? source, MermaidOptions? options = null) => Layout(Parse(source), options);

    public static string ToSvg(string? source, MermaidOptions? options = null, SvgOptions? svg = null) =>
        SvgRenderer.Render(Render(source, options), svg);

    private static MermaidDiagram Failed(DiagramKind kind, MermaidError error, MermaidConfig config) =>
        new(kind, null, config, null, null, null, error);

    private static DiagramScene ErrorScene(RenderContext context, DiagramKind kind, MermaidError error, string? background)
    {
        var canvas = context.CreateCanvas();
        var heading = context.MeasurePlain("Syntax error in diagram", 16, bold: true);
        var message = context.Measure(Text.RichText.Styled(error.Line > 0 ? $"Line {error.Line}: {error.Message}" : error.Message), 13, 420, forceWrap: true);
        var width = Math.Max(heading.Width, message.Width) + 32;
        var height = heading.Height + message.Height + 28;
        var color = context.Theme.Hex("errorTextColor", "#552222");
        canvas.Rect(0, 0, width, height, context.Theme.C("errorBkgColor").WithAlpha(0.08).ToString(), color, 1, 6);
        canvas.Label(heading, 16, 12, TextAnchor.Start, color, bold: true);
        canvas.Label(message, 16, 14 + heading.Height, TextAnchor.Start, color);
        return canvas.Finish(kind, null, null, 4, background, error);
    }
}
