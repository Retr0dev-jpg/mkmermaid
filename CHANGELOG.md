# Changelog

## 0.1.0

First release.

- Parsers for 23 Mermaid diagram types: flowchart, sequence, class, state, entity relationship, gantt, pie, user journey, git graph, quadrant, requirement, C4, mindmap, timeline, sankey, XY chart, block, packet, kanban, architecture, radar, treemap and ZenUML.
- Front matter, `%%{init}%%` directives, accessibility statements, the five Mermaid themes with theme variables, and `classDef`/`style` styling.
- Layered (Sugiyama, dagre-style) graph layout with nested clusters, per-cluster direction and edge label placement, available as a public API.
- Pluggable text measurement, a platform-neutral scene model and a self-contained SVG renderer.
- Snapshot corpus, behavior tests, and fuzz and deep-nesting tests that treat any internal error as a failure.
