# Contributing

Thank you for helping. Documentation, code and comments are in English.

## Layout

| Path | Contents |
| --- | --- |
| `src/MkMermaid/Parsing` | Source preprocessing (front matter, directives, comments, accessibility statements), diagram detection, parse helpers. |
| `src/MkMermaid/Config`, `src/MkMermaid/Theming` | Mermaid configuration (JSON and YAML readers), the five themes and their derived variables, colors. |
| `src/MkMermaid/Text` | Label syntax (markdown strings, `<br>`, entities), wrapping and the `ITextMeasurer` interface. |
| `src/MkMermaid/Layout` | `LayeredGraph` and `LayeredLayout`: layered (Sugiyama) layout with nested clusters and edge routing. |
| `src/MkMermaid/Rendering` | `RenderContext` and `Canvas` (scene builder), shapes, markers, edge drawing, curves, CSS styles. |
| `src/MkMermaid/Diagrams/<Type>` | One folder per diagram type: model, parser and renderer. `Parsers.cs` maps each `DiagramKind` to its parser. |
| `src/MkMermaid/Scene` | The public scene model and the SVG renderer. |
| `tests/MkMermaid.Tests` | Corpus snapshots, engine, diagram, layout, SVG, nesting and fuzz tests. |
| `tools/MkMermaid.Gallery` | HTML gallery of the corpus in the light and dark themes (`--filter`, `--bench`). |

## Rules

- `dotnet build MkMermaid.slnx -warnaserror` must pass with zero warnings, and `dotnet test tests/MkMermaid.Tests` must pass.
- `.cs`, `.csproj`, `.props`, `.slnx` and `.ps1` files are UTF-8 with BOM (see `.editorconfig`). Markdown, YAML, TSV and `.mmd` files have no BOM.
- Layout changes: render the gallery and compare it before and after. Then update `Corpus/snapshots.tsv` with `MKMERMAID_UPDATE_SNAPSHOTS=1`, and explain the visual change in the pull request.
- New syntax: follow the Mermaid documentation and add a corpus sample (`<type>-NN-<topic>.mmd`) or a test in `DiagramTests`. Update the "Supported diagrams" and "Not supported" sections of the README.
- Parsers report problems with `MermaidParseException` and the 1-based line number. Validation belongs in the parser, so that `Validate` and `Render` agree. Renderers only lay out a valid model.
- Keep the limits in `SECURITY.md` true. Every loop and recursion that depends on input must be bounded, and nesting uses the 32-level limit of the other parsers. The fuzz tests treat `MermaidErrorCode.Internal` as a failure: fix the cause, never hide it.
- Write comments only for constraints that the code does not make obvious.

## Releases

Bump `VersionPrefix` in `Directory.Build.props`, update `CHANGELOG.md`, then push a tag `vX.Y.Z`. The release workflow builds, tests and packs, creates a GitHub Release with the `.nupkg`, and publishes to nuget.org when the `NUGET_API_KEY` repository secret is set.
