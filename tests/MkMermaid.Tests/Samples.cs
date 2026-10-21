using System.Xml.Linq;
using MkMermaid.Scene;

namespace MkMermaid.Tests;

internal static class Samples
{
    public static IReadOnlyList<(string Name, string Source)> Corpus { get; } = Directory
        .GetFiles(Path.Combine(AppContext.BaseDirectory, "Corpus"), "*.mmd")
        .Order(StringComparer.Ordinal)
        .Select(p => (Path.GetFileNameWithoutExtension(p), File.ReadAllText(p).ReplaceLineEndings("\n")))
        .ToList();

    public static IReadOnlyList<string> Texts(string source, MermaidOptions? options = null) =>
        MermaidEngine.Render(source, options).Items.OfType<TextItem>().Select(t => t.Text).ToList();

    /// <summary>Fails on library defects, non-finite coordinates or SVG that is not well-formed XML.</summary>
    public static DiagramScene AssertSound(string source, MermaidOptions? options = null)
    {
        var scene = MermaidEngine.Render(source, options);
        Assert.False(scene.Error?.Code == MermaidErrorCode.Internal, $"{scene.Error?.Message}\n--- source ---\n{source}");
        Assert.True(double.IsFinite(scene.Width) && double.IsFinite(scene.Height) && scene.Width >= 0 && scene.Height >= 0, source);
        foreach (var item in scene.Items)
        {
            switch (item)
            {
                case ShapeItem shape:
                    Assert.True(double.IsFinite(shape.StrokeWidth) && double.IsFinite(shape.Opacity), source);
                    foreach (var c in shape.Geometry.Commands)
                    {
                        Assert.True(double.IsFinite(c.X) && double.IsFinite(c.Y) && double.IsFinite(c.X1) && double.IsFinite(c.Y1)
                            && double.IsFinite(c.X2) && double.IsFinite(c.Y2), source);
                    }

                    break;
                case TextItem text:
                    Assert.True(double.IsFinite(text.X) && double.IsFinite(text.Y) && double.IsFinite(text.FontSize) && double.IsFinite(text.Rotation), source);
                    break;
            }
        }

        _ = XDocument.Parse(SvgRenderer.Render(scene));
        return scene;
    }
}
