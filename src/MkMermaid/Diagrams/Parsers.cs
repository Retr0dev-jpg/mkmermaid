using MkMermaid.Diagrams.Architecture;
using MkMermaid.Diagrams.Block;
using MkMermaid.Diagrams.C4;
using MkMermaid.Diagrams.Class;
using MkMermaid.Diagrams.EntityRelationship;
using MkMermaid.Diagrams.Flowchart;
using MkMermaid.Diagrams.Gantt;
using MkMermaid.Diagrams.GitGraph;
using MkMermaid.Diagrams.Journey;
using MkMermaid.Diagrams.Kanban;
using MkMermaid.Diagrams.Mindmap;
using MkMermaid.Diagrams.Packet;
using MkMermaid.Diagrams.Pie;
using MkMermaid.Diagrams.Quadrant;
using MkMermaid.Diagrams.Radar;
using MkMermaid.Diagrams.Requirement;
using MkMermaid.Diagrams.Sankey;
using MkMermaid.Diagrams.Sequence;
using MkMermaid.Diagrams.State;
using MkMermaid.Diagrams.Timeline;
using MkMermaid.Diagrams.Treemap;
using MkMermaid.Diagrams.XyChart;
using MkMermaid.Parsing;

namespace MkMermaid.Diagrams;

internal static class Parsers
{
    public static IDiagramModel Parse(Source source) => source.Kind switch
    {
        DiagramKind.Flowchart => FlowchartParser.Parse(source),
        DiagramKind.Sequence => SequenceParser.Parse(source),
        DiagramKind.Class => ClassParser.Parse(source),
        DiagramKind.State => StateParser.Parse(source),
        DiagramKind.EntityRelationship => ErParser.Parse(source),
        DiagramKind.Gantt => GanttParser.Parse(source),
        DiagramKind.Pie => PieParser.Parse(source),
        DiagramKind.Journey => JourneyParser.Parse(source),
        DiagramKind.GitGraph => GitGraphParser.Parse(source),
        DiagramKind.Quadrant => QuadrantParser.Parse(source),
        DiagramKind.XyChart => XyChartParser.Parse(source),
        DiagramKind.Radar => RadarParser.Parse(source),
        DiagramKind.Packet => PacketParser.Parse(source),
        DiagramKind.Sankey => SankeyParser.Parse(source),
        DiagramKind.Timeline => TimelineParser.Parse(source),
        DiagramKind.Mindmap => MindmapParser.Parse(source),
        DiagramKind.Kanban => KanbanParser.Parse(source),
        DiagramKind.Treemap => TreemapParser.Parse(source),
        DiagramKind.Requirement => RequirementParser.Parse(source),
        DiagramKind.C4 => C4Parser.Parse(source),
        DiagramKind.Block => BlockParser.Parse(source),
        DiagramKind.Architecture => ArchitectureParser.Parse(source),
        DiagramKind.ZenUml => ZenUmlParser.Parse(source),
        _ => throw new MermaidParseException(MermaidErrorCode.UnknownDiagram, $"'{source.Keyword}' diagrams are not supported.", source.HeaderLine)
    };
}
