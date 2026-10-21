# Changelog

## 0.1.2

- Kanban columns and cards written as bare text may contain spaces (`In progress`), as in Mermaid.

## 0.1.1

- Entity codes such as `#59;` and `#124;` no longer end a statement, in flowcharts and at the end of sequence messages, as in Mermaid.
- Entity codes decode after the label markup is read, so `#60;b#62;` is drawn as the literal text `<b>` instead of turning on bold.

## 0.1.0

First release.

- Parsers for 23 Mermaid diagram types: flowchart, sequence, class, state, entity relationship, gantt, pie, user journey, git graph, quadrant, requirement, C4, mindmap, timeline, sankey, XY chart, block, packet, kanban, architecture, radar, treemap and ZenUML.
- Front matter, `%%{init}%%` directives, accessibility statements, the five Mermaid themes with theme variables, and `classDef`/`style` styling.
- Layered (Sugiyama, dagre-style) graph layout with nested clusters, per-cluster direction and edge label placement, available as a public API.
- Pluggable text measurement, a platform-neutral scene model and a self-contained SVG renderer.
- Snapshot corpus, behavior tests, and fuzz and deep-nesting tests that treat any internal error as a failure.
