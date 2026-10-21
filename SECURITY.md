# Security

MkMermaid is designed to lay out untrusted input, for example diagrams from documents you did not write.

## Guarantees

- `MermaidEngine.Parse`, `Validate`, `Layout`, `Render` and `ToSvg` do not throw for any input string; errors are returned as `MermaidError`. A defect inside the library is reported as `MermaidErrorCode.Internal` instead of an exception, and the fuzz tests fail whenever that code appears.
- Work is bounded:
  - sources longer than 50,000 characters are rejected;
  - a diagram holds at most 2,000 elements (nodes, participants, tasks, commits, flows and so on) and 4,000 connections;
  - blocks, subgraphs, states, namespaces, C4 boundaries, architecture groups and ZenUML bodies nest at most 32 levels, and configuration objects 32 levels; indentation-based trees (mindmap, treemap, kanban) need one more column per level, so the source length limits them to about 300 levels;
  - cluster nesting in the layered layout stops at 64 levels, and its ordering and ranking passes have fixed iteration counts;
  - configuration and theme numbers beyond ±100,000 fall back to their defaults, so sizes and spacings stay finite;
  - gantt `excludes` expands at most 10,000 days, and packet diagrams hold at most 65,536 bits.
- No file, network or process access. `click`, `href` and `link` statements are parsed but never followed, and icons are never downloaded.
- The SVG renderer writes only paths, text, title, description and gradient definitions: no scripts, styles, `foreignObject`, images or links. Text and attributes are escaped, and characters XML forbids are replaced with U+FFFD. Colors are normalized to `#RRGGBB(AA)` before they reach the scene.
- Nothing is logged. Error messages quote at most a short, truncated fragment of the offending statement, and `Internal` errors contain only the exception type.

## Reporting a vulnerability

Please report vulnerabilities privately through [GitHub security advisories](https://github.com/Retr0dev-jpg/mkmermaid/security/advisories/new) rather than in public issues. Include a minimal diagram that reproduces the problem. You will receive a reply within a week.
