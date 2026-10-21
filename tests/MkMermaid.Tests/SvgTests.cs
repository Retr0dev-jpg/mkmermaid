using System.Xml.Linq;
using MkMermaid.Layout;
using MkMermaid.Scene;

namespace MkMermaid.Tests;

public class SvgTests
{
    [Fact]
    public void MarkupInLabelsIsEscaped()
    {
        var svg = MermaidEngine.ToSvg("flowchart TD\n  A[\"<script>alert(1)</script>\"] --> B[\"x onload=alert(1)\"]");
        Assert.DoesNotContain("<script", svg, StringComparison.OrdinalIgnoreCase);
        _ = XDocument.Parse(svg);
    }

    [Fact]
    public void OutputHasNoActiveOrExternalContent()
    {
        foreach (var (_, source) in Samples.Corpus)
        {
            var svg = MermaidEngine.ToSvg(source);
            var document = XDocument.Parse(svg);
            foreach (var element in document.Descendants())
            {
                Assert.DoesNotContain(element.Name.LocalName, new[] { "script", "foreignObject", "image", "use", "a", "style", "iframe" });
                foreach (var attribute in element.Attributes())
                {
                    Assert.False(attribute.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase), attribute.ToString());
                    Assert.False(attribute.Name.LocalName == "href", attribute.ToString());
                }
            }
        }
    }

    [Fact]
    public void InvalidXmlCharactersAreReplaced()
    {
        var scene = new DiagramScene(DiagramKind.Flowchart, 10, 10, null,
            [new TextItem(0, 10, [new TextRun("a\u0001b\uD800c\uFFFE😀")], 12, TextAnchor.Start, "#000000")], "t\u0000", null, null, "Arial");
        var svg = SvgRenderer.Render(scene);
        var text = XDocument.Parse(svg).Descendants().Single(e => e.Name.LocalName == "text").Value;
        Assert.Equal("a\uFFFDb\uFFFDc\uFFFD😀", text);
    }

    [Fact]
    public void TitleAndDescriptionAreAccessible()
    {
        var svg = MermaidEngine.ToSvg("flowchart TD\n  accTitle: Order flow\n  accDescr: How an order moves\n  A --> B");
        var document = XDocument.Parse(svg);
        Assert.Equal("img", document.Root!.Attribute("role")?.Value);
        Assert.Contains(document.Root.Elements(), e => e.Name.LocalName == "desc" && e.Value == "How an order moves");
    }

    [Fact]
    public void ErrorsAreMarkedInvalid()
    {
        var svg = MermaidEngine.ToSvg("flowchart TD\n  A --> (");
        Assert.Equal("true", XDocument.Parse(svg).Root!.Attribute("aria-invalid")?.Value);
    }

    [Fact]
    public void NonFiniteCoordinatesBecomeZero()
    {
        var scene = new DiagramScene(DiagramKind.Flowchart, double.NaN, 10, null,
            [new ShapeItem(PathGeometry.Line(double.NaN, 0, double.PositiveInfinity, 1), null, "#000000", 1)], null, null, null, "Arial");
        var svg = SvgRenderer.Render(scene);
        Assert.DoesNotContain("NaN", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("Infinity", svg, StringComparison.Ordinal);
        Assert.Contains("M0 0L0 1", svg, StringComparison.Ordinal);
        _ = new Point(0, 0);
    }
}
