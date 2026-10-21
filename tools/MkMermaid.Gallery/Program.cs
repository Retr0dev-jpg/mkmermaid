using System.Diagnostics;
using System.Net;
using System.Text;
using MkMermaid;
using MkMermaid.Scene;

// Renders every sample in samples/*.mmd to an HTML page for visual review.
// Usage: MkMermaid.Gallery [output.html] [--filter text] [--bench]
var output = Path.Combine(Path.GetTempPath(), "mkgallery", "index.html");
string? filter = null;
var bench = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--filter" when i + 1 < args.Length:
            filter = args[++i];
            break;
        case "--bench":
            bench = true;
            break;
        default:
            output = args[i];
            break;
    }
}

var directory = Path.Combine(AppContext.BaseDirectory, "samples");
var files = Directory.GetFiles(directory, "*.mmd").Order(StringComparer.Ordinal)
    .Where(f => filter is null || Path.GetFileName(f).Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

if (bench)
{
    var sources = files.Select(File.ReadAllText).ToList();
    foreach (var s in sources)
    {
        MermaidEngine.Render(s);
    }

    var watch = Stopwatch.StartNew();
    const int rounds = 20;
    for (var r = 0; r < rounds; r++)
    {
        foreach (var s in sources)
        {
            MermaidEngine.Render(s);
        }
    }

    watch.Stop();
    Console.WriteLine($"{sources.Count} diagrams, {watch.Elapsed.TotalMilliseconds / (rounds * sources.Count):0.00} ms per diagram");
    return;
}

const string Header = "<!doctype html><meta charset=\"utf-8\"><title>MkMermaid gallery</title><style>"
    + "body{font-family:Segoe UI,sans-serif;margin:16px;background:#f3f3f3}"
    + ".s{background:#fff;margin:12px 0;padding:12px;border-radius:6px}.d{background:#1e1e1e;color:#ddd}"
    + "h2{font-size:14px;margin:0 0 8px}pre{font-size:11px;max-height:160px;overflow:auto;background:#f7f7f7;padding:6px}"
    + ".row{display:flex;gap:16px;align-items:flex-start;flex-wrap:wrap}.row>div{max-width:100%}"
    + "svg{max-width:100%;height:auto}.err{color:#b00}</style>";
var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(output))!;
Directory.CreateDirectory(outputDirectory);
var index = new StringBuilder(Header);
foreach (var file in files)
{
    var source = File.ReadAllText(file);
    var name = Path.GetFileNameWithoutExtension(file);
    var diagram = MermaidEngine.Parse(source);
    var light = MermaidEngine.Layout(diagram, new MermaidOptions());
    var dark = MermaidEngine.Layout(diagram, new MermaidOptions { Theme = "dark" });
    var section = new StringBuilder();
    section.Append("<div class=\"s\"><h2>").Append(WebUtility.HtmlEncode(name)).Append(" — ").Append(diagram.Kind);
    if (diagram.Error is { } error)
    {
        section.Append(" <span class=\"err\">").Append(WebUtility.HtmlEncode($"{error.Code} line {error.Line}: {error.Message}")).Append("</span>");
    }

    section.Append("</h2><div class=\"row\"><div>").Append(SvgRenderer.Render(light, new SvgOptions { Standalone = false }))
        .Append("</div><div class=\"d\">").Append(SvgRenderer.Render(dark, new SvgOptions { Standalone = false }))
        .Append("</div></div><pre>").Append(WebUtility.HtmlEncode(source)).Append("</pre></div>");
    index.Append(section);
    File.WriteAllText(Path.Combine(outputDirectory, name + ".html"), Header + section);
}

File.WriteAllText(output, index.ToString());
Console.WriteLine($"{files.Count} samples -> {output}");
