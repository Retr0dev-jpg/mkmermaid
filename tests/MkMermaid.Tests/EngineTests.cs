using System.Diagnostics;
using System.Text;

namespace MkMermaid.Tests;

public class EngineTests
{
    [Theory]
    [InlineData("flowchart TD", DiagramKind.Flowchart)]
    [InlineData("graph LR", DiagramKind.Flowchart)]
    [InlineData("flowchart-elk TD", DiagramKind.Flowchart)]
    [InlineData("sequenceDiagram", DiagramKind.Sequence)]
    [InlineData("classDiagram", DiagramKind.Class)]
    [InlineData("classDiagram-v2", DiagramKind.Class)]
    [InlineData("stateDiagram", DiagramKind.State)]
    [InlineData("stateDiagram-v2", DiagramKind.State)]
    [InlineData("erDiagram", DiagramKind.EntityRelationship)]
    [InlineData("gantt", DiagramKind.Gantt)]
    [InlineData("pie", DiagramKind.Pie)]
    [InlineData("journey", DiagramKind.Journey)]
    [InlineData("gitGraph", DiagramKind.GitGraph)]
    [InlineData("quadrantChart", DiagramKind.Quadrant)]
    [InlineData("requirementDiagram", DiagramKind.Requirement)]
    [InlineData("C4Context", DiagramKind.C4)]
    [InlineData("C4Deployment", DiagramKind.C4)]
    [InlineData("mindmap", DiagramKind.Mindmap)]
    [InlineData("timeline", DiagramKind.Timeline)]
    [InlineData("sankey-beta", DiagramKind.Sankey)]
    [InlineData("xychart-beta", DiagramKind.XyChart)]
    [InlineData("block-beta", DiagramKind.Block)]
    [InlineData("packet-beta", DiagramKind.Packet)]
    [InlineData("kanban", DiagramKind.Kanban)]
    [InlineData("architecture-beta", DiagramKind.Architecture)]
    [InlineData("radar-beta", DiagramKind.Radar)]
    [InlineData("treemap-beta", DiagramKind.Treemap)]
    [InlineData("zenuml", DiagramKind.ZenUml)]
    [InlineData("%% comment\n\nflowchart TD", DiagramKind.Flowchart)]
    [InlineData("---\ntitle: T\n---\npie", DiagramKind.Pie)]
    [InlineData("%%{init: {\"theme\": \"dark\"}}%%\nsequenceDiagram", DiagramKind.Sequence)]
    [InlineData("nonsense", DiagramKind.Unknown)]
    public void DetectsTheDiagramKind(string source, DiagramKind expected) => Assert.Equal(expected, MermaidEngine.Detect(source));

    [Fact]
    public void FrontMatterTitleIsDrawn()
    {
        var texts = Samples.Texts("---\ntitle: Quarterly plan\n---\nflowchart TD\n  A --> B");
        Assert.Contains("Quarterly plan", texts);
    }

    [Fact]
    public void DirectiveThemeAppliesUnlessForced()
    {
        const string source = "%%{init: {\"theme\": \"dark\"}}%%\nflowchart TD\n  A --> B";
        var requested = MermaidEngine.Render(source, new MermaidOptions { PaintBackground = true });
        var forced = MermaidEngine.Render(source, new MermaidOptions { PaintBackground = true, ForceTheme = true });
        Assert.NotEqual(requested.Background, forced.Background);
        Assert.Equal(MermaidEngine.Render("flowchart TD\n  A --> B", new MermaidOptions { PaintBackground = true }).Background, forced.Background);
    }

    [Fact]
    public void ErrorsPointAtTheOriginalLine()
    {
        var error = MermaidEngine.Validate("flowchart TD\n  %% comment\n  A --> B\n  B --> (");
        Assert.NotNull(error);
        Assert.Equal(4, error.Line);
    }

    [Fact]
    public void UnknownDiagramIsReported()
    {
        var error = MermaidEngine.Validate("notADiagram\n  A --> B");
        Assert.Equal(MermaidErrorCode.UnknownDiagram, error?.Code);
    }

    [Fact]
    public void OverlongSourceIsRejected()
    {
        var source = "flowchart TD\n" + new string('x', MermaidEngine.MaxSourceLength);
        Assert.Equal(MermaidErrorCode.TooLong, MermaidEngine.Validate(source)?.Code);
    }

    [Fact]
    public void TooManyElementsAreRejected()
    {
        var source = new StringBuilder("flowchart TD\n");
        for (var i = 0; i <= MermaidEngine.MaxElements; i++)
        {
            source.Append("  n").Append(i).Append('\n');
        }

        Assert.Equal(MermaidErrorCode.TooComplex, MermaidEngine.Validate(source.ToString())?.Code);
    }

    [Fact]
    public void ExtremeConfigNumbersFallBackToDefaults()
    {
        const string body = "flowchart TD\n  A --> B\n  A --> C";
        var normal = MermaidEngine.Render(body);
        var extreme = MermaidEngine.Render("%%{init: {\"flowchart\": {\"nodeSpacing\": 1e9, \"rankSpacing\": -1e308}}}%%\n" + body);
        Assert.Null(extreme.Error);
        Assert.Equal(normal.Width, extreme.Width);
        Assert.Equal(normal.Height, extreme.Height);
    }

    [Fact]
    public void LargeGraphLaysOutQuickly()
    {
        var source = new StringBuilder("flowchart TD\n");
        var random = new Random(3);
        for (var i = 0; i < 1500; i++)
        {
            source.Append("  n").Append(random.Next(400)).Append(" --> n").Append(random.Next(400)).Append('\n');
        }

        var watch = Stopwatch.StartNew();
        var scene = Samples.AssertSound(source.ToString());
        watch.Stop();
        Assert.Null(scene.Error);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"Layout took {watch.Elapsed}.");
    }

    [Fact]
    public void ErrorSceneNeverEchoesMoreThanTheMessage()
    {
        var scene = MermaidEngine.Render("flowchart TD\n  A --> (secret");
        Assert.NotNull(scene.Error);
        Assert.DoesNotContain(scene.Items.OfType<Scene.TextItem>(), t => t.Text.Contains("secret", StringComparison.Ordinal) && !t.Text.StartsWith("Line", StringComparison.Ordinal));
    }
}
