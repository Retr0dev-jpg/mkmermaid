namespace MkMermaid.Tests;

/// <summary>Behaviors taken from the Mermaid documentation, checked through the drawn text.</summary>
public class DiagramTests
{
    private static IReadOnlyList<string> Ok(string source)
    {
        var scene = Samples.AssertSound(source);
        Assert.True(scene.Error is null, scene.Error?.Message);
        return Samples.Texts(source);
    }

    [Fact]
    public void FlowchartLabelsAndEdgeTexts()
    {
        var texts = Ok("flowchart LR\n  A[Start] -->|yes| B(Done)\n  A -- no --> C{Retry?}");
        Assert.Contains("Start", texts);
        Assert.Contains("yes", texts);
        Assert.Contains("no", texts);
        Assert.Contains("Retry?", texts);
    }

    [Fact]
    public void SequenceAutonumber()
    {
        var texts = Ok("sequenceDiagram\n  autonumber\n  Alice->>Bob: Hi\n  Bob-->>Alice: Hello");
        Assert.Contains("1", texts);
        Assert.Contains("2", texts);
    }

    [Fact]
    public void PieShowsDataWhenAsked()
    {
        var texts = Ok("pie showData\n  title Pets\n  \"Dogs\" : 386\n  \"Cats\" : 85");
        Assert.Contains(texts, t => t.Contains("Dogs", StringComparison.Ordinal) && t.Contains("386", StringComparison.Ordinal));
        Assert.Contains("Pets", texts);
    }

    [Fact]
    public void GanttRejectsUnknownDependencies()
    {
        var error = MermaidEngine.Validate("gantt\n  dateFormat YYYY-MM-DD\n  section S\n  A :a1, after missing, 3d");
        Assert.NotNull(error);
        Assert.Equal(4, error.Line);
    }

    [Fact]
    public void BlockEdgesAcceptBothLabelForms()
    {
        var texts = Ok("block-beta\n  columns 3\n  a b c\n  a -- \"SQL\" --> b\n  b -->|HTTP| c");
        Assert.Contains("SQL", texts);
        Assert.Contains("HTTP", texts);
    }

    [Fact]
    public void BlockSpansAndNestedBlocks()
    {
        var texts = Ok("block-beta\n  columns 3\n  wide[\"Wide\"]:3\n  block:inner:2\n    x y\n  end\n  z");
        Assert.Contains("Wide", texts);
        Assert.Contains("x", texts);
    }

    [Fact]
    public void ZenUmlExplicitReturnIsNotDuplicated()
    {
        var texts = Ok("zenuml\n  A.call() {\n    result = B.compute() {\n      return value\n    }\n  }");
        Assert.Single(texts, "value");
        Assert.DoesNotContain("result", texts);
    }

    [Fact]
    public void ZenUmlAssignmentDrawsReturn()
    {
        var texts = Ok("zenuml\n  A.call() {\n    total = B.sum(1, 2)\n  }");
        Assert.Contains("sum(1, 2)", texts);
        Assert.Contains("total", texts);
    }

    [Fact]
    public void ZenUmlStateDoesNotLeakBetweenParses()
    {
        Ok("zenuml\n  A.m() {\n    B.n() {\n      return x\n    }\n  }");
        var texts = Ok("zenuml\n  return y");
        Assert.DoesNotContain("y", texts);
    }

    [Fact]
    public void ZenUmlUnbalancedBracesAreErrors()
    {
        Assert.NotNull(MermaidEngine.Validate("zenuml\n  A.m() {\n    B.n()"));
        Assert.NotNull(MermaidEngine.Validate("zenuml\n  }"));
    }

    [Fact]
    public void EmptyBetaDiagramsRender()
    {
        foreach (var header in new[] { "architecture-beta", "block-beta", "kanban", "mindmap", "timeline", "radar-beta", "treemap-beta", "packet-beta", "sankey-beta", "xychart-beta", "quadrantChart", "requirementDiagram", "C4Context", "zenuml" })
        {
            var scene = Samples.AssertSound(header);
            Assert.False(scene.Error?.Code == MermaidErrorCode.Internal, header);
        }
    }

    [Fact]
    public void ArchitectureEdgesStartBelowLabels()
    {
        var scene = Samples.AssertSound("architecture-beta\n  service a(server)[Top]\n  service b(disk)[Bottom]\n  a:B -- T:b");
        var label = scene.Items.OfType<Scene.TextItem>().Single(t => t.Text == "Top");
        var edge = scene.Items.OfType<Scene.ShapeItem>().First(s => s.Fill is null && s.Geometry.Commands.Count == 2);
        Assert.True(edge.Geometry.Commands.Min(c => c.Y) > label.Y, "The edge crosses the service label.");
    }

    [Fact]
    public void ArchitectureGroupCyclesAreErrors()
    {
        var error = MermaidEngine.Validate("architecture-beta\n  group a(cloud)[A] in b\n  group b(cloud)[B] in c\n  group c(cloud)[C] in b");
        Assert.Equal(MermaidErrorCode.InvalidValue, error?.Code);
    }

    [Fact]
    public void ColorsInTheSceneAreNormalized()
    {
        var scene = Samples.AssertSound("C4Context\n  Person(p, \"P\")\n  UpdateElementStyle(p, $bgColor=\"url(javascript:x)\", $fontColor=\"red\")");
        Assert.All(scene.Items.OfType<Scene.ShapeItem>(), s => Assert.True(s.Fill is null || s.Fill.StartsWith('#'), s.Fill));
        Assert.All(scene.Items.OfType<Scene.TextItem>(), t => Assert.StartsWith("#", t.Color, StringComparison.Ordinal));
    }

    [Fact]
    public void SankeyCyclesAreErrors()
    {
        Assert.NotNull(MermaidEngine.Validate("sankey-beta\nA,B,1\nB,A,1"));
    }

    [Fact]
    public void PacketRejectsGaps()
    {
        Assert.NotNull(MermaidEngine.Validate("packet-beta\n  0-7: \"A\"\n  9-15: \"B\""));
    }

    [Fact]
    public void C4RelationshipShowsTechnology()
    {
        var texts = Ok("C4Context\n  Person(u, \"User\")\n  System(s, \"Shop\")\n  Rel(u, s, \"Buys\", \"HTTPS\")");
        Assert.Contains("Buys", texts);
        Assert.Contains("[HTTPS]", texts);
    }

    [Fact]
    public void RequirementRelationsAreLabeled()
    {
        var texts = Ok("requirementDiagram\n  requirement r {\n    id: 1\n  }\n  element e {\n    type: doc\n  }\n  e - satisfies -> r");
        Assert.Contains("<<satisfies>>", texts);
    }
}
