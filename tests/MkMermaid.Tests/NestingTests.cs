using System.Text;

namespace MkMermaid.Tests;

/// <summary>Deep nesting within the source length limit must end in an error, never in a stack overflow.</summary>
public class NestingTests
{
    private const int Levels = 5_000;

    public static TheoryData<string> Sources()
    {
        static string Repeat(string header, string open, string close, Func<int, string>? indent = null)
        {
            var text = new StringBuilder(header).Append('\n');
            for (var i = 0; i < Levels && text.Length < MermaidEngine.MaxSourceLength / 2; i++)
            {
                text.Append(indent?.Invoke(i) ?? "").Append(open.Replace("#", i.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)).Append('\n');
            }

            for (var i = 0; i < Levels && text.Length < MermaidEngine.MaxSourceLength - 10 && close.Length > 0; i++)
            {
                text.Append(close).Append('\n');
            }

            return text.ToString()[..Math.Min(text.Length, MermaidEngine.MaxSourceLength - 1)];
        }

        return new TheoryData<string>
        {
            Repeat("flowchart TD", "subgraph s#", "end"),
            Repeat("stateDiagram-v2", "state s# {", "}"),
            Repeat("classDiagram", "namespace n# {", "}"),
            Repeat("sequenceDiagram", "loop l", "end"),
            Repeat("block-beta", "block:b#", "end"),
            Repeat("C4Context", "Boundary(b#, \"B\") {", "}"),
            Repeat("zenuml", "A.m() {", "}"),
            Repeat("zenuml", "if (x) {", "}"),
            Repeat("architecture-beta", "group g#(cloud)[G] in g#", ""),
            Repeat("mindmap", "n#", "", i => new string(' ', Math.Min(i, 8000))),
            Repeat("treemap-beta", "\"n#\"", "", i => new string(' ', Math.Min(i, 8000))),
            Repeat("requirementDiagram", "requirement r# {", "}"),
            "zenuml\n" + new string('{', MermaidEngine.MaxSourceLength - 10),
            "flowchart TD\n  A[" + new string('[', 20_000) + "]",
            "%%{init: " + new string('{', 20_000) + "}%%\nflowchart TD",
            "---\nconfig:\n" + string.Concat(Enumerable.Range(0, 2_000).Select(i => new string(' ', i) + "a:\n")) + "---\nflowchart TD"
        };
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public void DeepNestingIsBounded(string source) => Samples.AssertSound(source);
}
