using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using MkMermaid.Scene;

namespace MkMermaid.Tests;

/// <summary>
/// Renders every corpus sample in the light and dark themes and compares SHA-256 hashes of the SVG with
/// Corpus/snapshots.tsv. Set MKMERMAID_UPDATE_SNAPSHOTS=1 to rewrite the file after reviewing the gallery.
/// </summary>
public class CorpusTests
{
    private static string SourceDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

    [Fact]
    public void EveryDiagramKindHasASample()
    {
        var covered = Samples.Corpus.Select(s => MermaidEngine.Detect(s.Source)).ToHashSet();
        var missing = Enum.GetValues<DiagramKind>().Where(k => k != DiagramKind.Unknown && !covered.Contains(k)).ToList();
        Assert.True(missing.Count == 0, "No corpus sample for: " + string.Join(", ", missing));
    }

    [Fact]
    public void SamplesRenderWithoutErrors()
    {
        var failures = new List<string>();
        foreach (var (name, source) in Samples.Corpus)
        {
            foreach (var theme in MermaidEngine.Themes)
            {
                var scene = Samples.AssertSound(source, new MermaidOptions { Theme = theme });
                if (scene.Error is { } error)
                {
                    failures.Add($"{name} ({theme}): line {error.Line}: {error.Message}");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    [Fact]
    public void SnapshotsMatch()
    {
        var actual = new StringBuilder();
        foreach (var (name, source) in Samples.Corpus)
        {
            foreach (var theme in new[] { "default", "dark" })
            {
                var svg = MermaidEngine.ToSvg(source, new MermaidOptions { Theme = theme });
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(svg)))[..16];
                actual.Append(hash).Append('\t').Append(name).Append('\t').Append(theme).Append('\n');
            }
        }

        if (Environment.GetEnvironmentVariable("MKMERMAID_UPDATE_SNAPSHOTS") == "1")
        {
            File.WriteAllText(Path.Combine(SourceDirectory(), "Corpus", "snapshots.tsv"), actual.ToString());
            return;
        }

        // CI builds map source paths to /_/, so the comparison reads the copy in the output directory.
        var path = Path.Combine(AppContext.BaseDirectory, "Corpus", "snapshots.tsv");
        Assert.True(File.Exists(path), "Corpus/snapshots.tsv is missing; run the tests with MKMERMAID_UPDATE_SNAPSHOTS=1.");
        var expected = File.ReadAllText(path).ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var changed = actual.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => !expected.Contains(l)).ToList();
        Assert.True(changed.Count == 0, $"{changed.Count} snapshots changed:\n" + string.Join('\n', changed.Take(20)));
    }

    [Fact]
    public void LayoutIsDeterministic()
    {
        foreach (var (_, source) in Samples.Corpus)
        {
            Assert.Equal(MermaidEngine.ToSvg(source), MermaidEngine.ToSvg(source));
        }
    }

    [Fact]
    public void GradientIdsUseThePrefix()
    {
        var sankey = Samples.Corpus.First(s => MermaidEngine.Detect(s.Source) == DiagramKind.Sankey).Source;
        var svg = MermaidEngine.ToSvg(sankey, null, new SvgOptions { IdPrefix = "note7" });
        Assert.Contains("id=\"note7-g0\"", svg, StringComparison.Ordinal);
        Assert.Contains("url(#note7-g0)", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("mkm-g", svg, StringComparison.Ordinal);
    }
}
