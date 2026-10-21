# MkMermaid

Native Mermaid diagrams for .NET. MkMermaid parses Mermaid source, lays it out, and returns a platform-neutral scene of paths and text. An SVG renderer is included. There is no browser, no JavaScript, and no native dependency.

- Targets `net8.0` and `net10.0` and uses only managed code.
- Covers 23 diagram types, together with Mermaid 11's configuration, directives, front matter, themes and `classDef`/`style` statements.
- Text measurement is pluggable (`ITextMeasurer`), so labels wrap and fit with the fonts your UI actually uses.
- Robust against untrusted input. Invalid input never throws: the scene carries a `MermaidError` and draws an error box. See [SECURITY.md](SECURITY.md) for the limits.

## Install

```powershell
dotnet add package MkMermaid
```

## Usage

```csharp
using MkMermaid;
using MkMermaid.Scene;

const string source = """
    flowchart LR
      A[Write] --> B{Valid?}
      B -->|yes| C[Publish]
      B -->|no| A
    """;

var diagram = MermaidEngine.Parse(source);          // cheap; no text measurement
if (diagram.Error is { } error)
{
    Console.WriteLine($"{error.Code} at line {error.Line}: {error.Message}");
}

var scene = MermaidEngine.Layout(diagram, new MermaidOptions { Theme = "dark", FontSize = 16 });
string svg = SvgRenderer.Render(scene, new SvgOptions { IdPrefix = "diagram1" });
```

`DiagramScene` has a `Width`, a `Height`, an optional `Background`, and `Items` to draw in order. Coordinates are pixels; y grows downward.

| Item | Meaning |
| --- | --- |
| `ShapeItem` | A `PathGeometry` (move, line, cubic, close) with an optional fill, stroke, dash pattern, opacity, and stroke gradient (`LinearGradient`, used by sankey links). |
| `TextItem` | One line of text at its alphabetic baseline, made of styled runs (bold, italic, underline, strike), with an anchor and a rotation in degrees around (X, Y). |

Colors are `#RRGGBB` or `#RRGGBBAA`. To draw natively (WinUI, WPF, Avalonia, Skia), map each path command to the platform geometry and each text item to a text run. Pass an `ITextMeasurer` that uses the same font as your renderer, so the layout matches what you draw. `DefaultTextMeasurer` uses Arial metrics and needs no font files.

Other options:

- `MermaidOptions.Theme` (`default`, `base`, `dark`, `forest`, `neutral`) applies unless the diagram asks for its own theme. `ForceTheme` ignores the diagram's theme and theme variables, and `ThemeVariables` adds host overrides.
- `MermaidOptions.PaintBackground` adds the theme background.
- `SvgOptions.IdPrefix` keeps gradient ids unique when several diagrams share one HTML page.
- `MermaidEngine.Detect` returns the `DiagramKind` without parsing, and `MermaidEngine.Validate` returns the first error.

The layered graph layout is public too. Build a `LayeredGraph` (nodes, nested clusters, edges with label sizes, a direction and spacing) and call `LayeredLayout.Compute`. You get node boxes, cluster boxes, and edge routes with label positions.

## Supported diagrams

| Diagram | Header | Notes |
| --- | --- | --- |
| Flowchart | `flowchart`, `graph` | All classic shapes and the `@{ shape: … }` syntax, edge types and lengths, labels, subgraphs with their own direction, `classDef`/`class`/`style`/`linkStyle`, markdown strings, edge data (`e1@{ curve: … }`). |
| Sequence | `sequenceDiagram` | Participants, actors and the other participant kinds, boxes, every arrow type, activations, notes, `loop`/`alt`/`opt`/`par`/`critical`/`break`/`rect`, autonumber, create and destroy. |
| Class | `classDiagram` | Members, visibility, generics, annotations, relations with cardinality, namespaces, notes, direction. |
| State | `stateDiagram-v2` | Composite states, concurrency, choice, fork and join, notes, direction, styles. |
| Entity relationship | `erDiagram` | Attributes with keys and comments, every cardinality, identifying and non-identifying relations. |
| Gantt | `gantt` | `dateFormat`, `axisFormat`, `tickInterval`, `after`/`until`, `excludes`, weekends, milestones, `done`/`active`/`crit`, compact mode, today marker. |
| Pie | `pie` | `showData`, `textPosition`. |
| User journey | `journey` | Sections, scores and actors. |
| Git graph | `gitGraph` | Branches, checkout, merge, cherry-pick, tags, commit types, orientations, parallel commits. |
| Quadrant | `quadrantChart` | Axes, quadrant labels, points with styles and classes. |
| Requirement | `requirementDiagram` | Every requirement kind, elements, relations, direction, styles. |
| C4 | `C4Context`, `C4Container`, `C4Component`, `C4Dynamic`, `C4Deployment` | People, systems, containers, components, every boundary, relations, `Update*Style`, `UpdateLayoutConfig`. |
| Mindmap | `mindmap` | Every node shape. |
| Timeline | `timeline` | Sections, periods and events. |
| Sankey | `sankey-beta` | CSV input, node alignment, link colors (gradient, source, target, fixed). |
| XY chart | `xychart-beta` | Bars and lines, category and numeric axes, horizontal orientation, data labels. |
| Block | `block-beta` | Columns, spans, nested blocks, space, block arrows, edges, styles. |
| Packet | `packet-beta` | Bit ranges and `+count` fields, bits per row. |
| Kanban | `kanban` | Columns, cards, ticket, assignee and priority metadata. |
| Architecture | `architecture-beta` | Groups, services, junctions, edges with sides and arrows; built-in icons (cloud, database, disk, internet, server). |
| Radar | `radar-beta` | Axes, curves, ticks, min/max, polygon or circle graticule, legend. |
| Treemap | `treemap-beta` | Nested sections, `classDef`, value formats. |
| ZenUML | `zenuml` | Participants and annotators, sync and async messages, nested calls, assignments and returns, `new`, `if`/`else`, loops, `opt`, `par`, `try`/`catch`/`finally`, groups. |

## Not supported

- Interaction: `click` callbacks and links are parsed but do nothing, and tooltips are not shown.
- Font Awesome and Iconify icons. `fa:fa-…` tokens are removed from labels, mindmap `::icon()` and architecture icon packs are ignored, and unknown architecture icons appear as a question mark.
- Mindmap `:::class` styling, and the C4 `$sprite` and `$link` attributes.
- Arbitrary CSS: only the properties `classDef` and `style` commonly use (fill, stroke, stroke-width, stroke-dasharray, color, font-weight, font-style, opacity) are applied.
- The ELK and tidy-tree layouts. `flowchart-elk` and `layout: elk` use the layered layout.
- Layouts are close to Mermaid's but not pixel-identical. Long labels wrap with your fonts, not with a browser's.

## Development

```powershell
dotnet build MkMermaid.slnx -warnaserror
dotnet test tests/MkMermaid.Tests
dotnet run --project tools/MkMermaid.Gallery -- --filter sankey    # writes %TEMP%\mkgallery\*.html for visual review
```

`tests/MkMermaid.Tests/Corpus` holds one or more samples per diagram type. Their SVG hashes, in the default and dark themes, are stored in `snapshots.tsv`. After an intentional layout change, review the gallery first, then update the snapshots:

```powershell
$env:MKMERMAID_UPDATE_SNAPSHOTS = "1"; dotnet test tests/MkMermaid.Tests; Remove-Item Env:MKMERMAID_UPDATE_SNAPSHOTS
```

The fuzz tests run thousands of seeded mutations on every build. Set `MKMERMAID_FUZZ_SCALE=20` for a longer local run.

See [CONTRIBUTING.md](CONTRIBUTING.md) and [SECURITY.md](SECURITY.md).

## License

MkMermaid is licensed under the [GNU Lesser General Public License v3.0](LICENSE). You can use the package from applications under any license: when you ship MkMermaid unmodified as a separate DLL, the LGPL obligations are met by keeping its notices and letting users replace the DLL. Modifications to MkMermaid itself must be released under the LGPL.

MkMermaid is an independent implementation of the Mermaid syntax. It is not affiliated with the Mermaid project. Ported algorithms and default values are credited in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
