using System.Text;

namespace MkMermaid.Tests;

/// <summary>Seeded mutations of the corpus and random statements; any library exception or non-finite output fails.</summary>
public class FuzzTests
{
    /// <summary>MKMERMAID_FUZZ_SCALE=N runs N times as many cases (for local soak runs).</summary>
    private static readonly int Scale = int.TryParse(Environment.GetEnvironmentVariable("MKMERMAID_FUZZ_SCALE"), out var s) && s > 0 ? s : 1;

    private static readonly string[] Pieces =
    [
        "-->", "---", "-.->", "==>", "--o", "<-->", "->>", "-->>", "-x", "|", "||", "{", "}", "{{", "}}", "[", "]", "[[", "]]", "(", ")",
        "((", "))", "[(", ")]", ">", "<", "\"", "'", ":", ";", ",", "::", ":::", "@{", "end", "else", "and", "subgraph s", "direction LR",
        "classDef c fill:#f00", "class a c", "style a fill:#zzz,stroke-width:-4px", "linkStyle 99 stroke:red", "click a call f()",
        "%%{init: {\"theme\": \"dark\"}}%%", "%%{init: {\"flowchart\": {\"nodeSpacing\": -1e308}}}%%", "%% note", "---\ntitle: t\n---",
        "0", "-1", "1e308", "-1e308", "NaN", "Infinity", "999999999999", "0.0000001", "100%", "2024-01-01", "1d", "after x",
        "a", "b", "Ab_1", "é", "日本", "😀", "\uD800", "\uDC00", "\u0000", "\u0007", "\u202E", "&amp;", "<b>x</b>", "<script>", "]]>",
        "\t", "  ", "    ", "\n", "\n    ", "\\n", "#quot;", "#9829;", "`**md**`", "fa:fa-car", "::icon(x)", "+8", "0-3", "x-axis", "y-axis",
        "quadrant-1", "title", "section", "participant", "actor", "activate a", "deactivate a", "loop", "alt", "opt", "par", "note right of a",
        "commit", "branch b", "checkout b", "merge b", "cherry-pick id: \"x\"", "space", "columns 0", "columns 100", "block:b", "service s(server)",
        "group g(cloud)", "junction j", "s:L -- R:t", "Rel(a, b, \"x\")", "Person(p, \"P\")", "Boundary(b, \"B\") {", "A.m() {", "return x", "@Actor A"
    ];

    private static readonly string[] Headers =
    [
        "flowchart TD", "graph LR", "sequenceDiagram", "classDiagram", "stateDiagram-v2", "erDiagram", "gantt", "pie showData", "journey",
        "gitGraph", "quadrantChart", "requirementDiagram", "C4Context", "C4Container", "C4Dynamic", "C4Deployment", "mindmap", "timeline",
        "sankey-beta", "xychart-beta", "block-beta", "packet-beta", "kanban", "architecture-beta", "radar-beta", "treemap-beta", "zenuml"
    ];

    [Fact]
    public void CorpusMutationsNeverThrow()
    {
        var random = new Random(20261020);
        foreach (var (_, source) in Samples.Corpus)
        {
            for (var i = 0; i < 60 * Scale; i++)
            {
                Samples.AssertSound(Mutate(source, random));
            }
        }
    }

    [Fact]
    public void RandomStatementsNeverThrow()
    {
        var random = new Random(7);
        for (var i = 0; i < 3000 * Scale; i++)
        {
            var builder = new StringBuilder(Headers[random.Next(Headers.Length)]).Append('\n');
            var lines = random.Next(1, 12);
            for (var l = 0; l < lines; l++)
            {
                var count = random.Next(1, 7);
                for (var j = 0; j < count; j++)
                {
                    builder.Append(Pieces[random.Next(Pieces.Length)]).Append(random.Next(3) == 0 ? "" : " ");
                }

                builder.Append('\n');
            }

            Samples.AssertSound(builder.ToString());
        }
    }

    [Fact]
    public void RandomCharactersNeverThrow()
    {
        var random = new Random(11);
        for (var i = 0; i < 1500 * Scale; i++)
        {
            var chars = new char[random.Next(1, 80)];
            for (var j = 0; j < chars.Length; j++)
            {
                chars[j] = (char)random.Next(0x09, 0x7F);
            }

            Samples.AssertSound(Headers[random.Next(Headers.Length)] + "\n" + new string(chars));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t")]
    [InlineData("\uFEFF")]
    [InlineData("%%{init: {")]
    [InlineData("---\ntitle: never closed")]
    [InlineData("%%{init: {\"theme\": \"forest\"}}%%")]
    [InlineData("notADiagram")]
    public void DegenerateInputsReportErrors(string? source)
    {
        var scene = MermaidEngine.Render(source);
        Assert.NotNull(scene.Error);
        Assert.NotEqual(MermaidErrorCode.Internal, scene.Error.Code);
    }

    private static string Mutate(string source, Random random)
    {
        var lines = source.Split('\n').ToList();
        var steps = random.Next(1, 4);
        for (var s = 0; s < steps && lines.Count > 0; s++)
        {
            var at = random.Next(lines.Count);
            switch (random.Next(7))
            {
                case 0:
                    lines.RemoveAt(at);
                    break;
                case 1:
                    lines.Insert(at, lines[random.Next(lines.Count)]);
                    break;
                case 2:
                    (lines[at], lines[random.Next(lines.Count)]) = (lines[random.Next(lines.Count)], lines[at]);
                    break;
                case 3:
                    lines[at] = lines[at][..random.Next(lines[at].Length + 1)];
                    break;
                case 4:
                    var position = random.Next(lines[at].Length + 1);
                    lines[at] = lines[at].Insert(position, Pieces[random.Next(Pieces.Length)]);
                    break;
                case 5:
                    lines.Insert(Math.Max(1, at), "    " + Pieces[random.Next(Pieces.Length)] + " " + Pieces[random.Next(Pieces.Length)]);
                    break;
                default:
                    var text = string.Join('\n', lines);
                    return text[..random.Next(text.Length + 1)];
            }
        }

        return string.Join('\n', lines);
    }
}
