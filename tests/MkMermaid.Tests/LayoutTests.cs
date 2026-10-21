using MkMermaid.Layout;

namespace MkMermaid.Tests;

public class LayoutTests
{
    private static LayeredGraph Chain(LayoutDirection direction)
    {
        var graph = new LayeredGraph { Direction = direction };
        graph.AddNode("a", 40, 20);
        graph.AddNode("b", 40, 20);
        graph.AddNode("c", 40, 20);
        graph.AddEdge("a", "b");
        graph.AddEdge("b", "c");
        return graph;
    }

    [Theory]
    [InlineData(LayoutDirection.TopToBottom)]
    [InlineData(LayoutDirection.BottomToTop)]
    [InlineData(LayoutDirection.LeftToRight)]
    [InlineData(LayoutDirection.RightToLeft)]
    public void RanksFollowTheDirection(LayoutDirection direction)
    {
        var layout = LayeredLayout.Compute(Chain(direction));
        double Along(string id)
        {
            var r = layout.Nodes[id].Bounds;
            return direction switch
            {
                LayoutDirection.TopToBottom => r.CenterY,
                LayoutDirection.BottomToTop => -r.CenterY,
                LayoutDirection.LeftToRight => r.CenterX,
                _ => -r.CenterX
            };
        }

        Assert.True(Along("a") < Along("b") && Along("b") < Along("c"));
    }

    [Fact]
    public void NodesInARankDoNotOverlap()
    {
        var graph = new LayeredGraph();
        graph.AddNode("root", 50, 30);
        for (var i = 0; i < 6; i++)
        {
            graph.AddNode("n" + i, 30 + 10 * i, 30);
            graph.AddEdge("root", "n" + i);
        }

        var boxes = LayeredLayout.Compute(graph).Nodes.Values.Where(n => n.Id != "root").Select(n => n.Bounds).OrderBy(r => r.X).ToList();
        for (var i = 1; i < boxes.Count; i++)
        {
            Assert.True(boxes[i].X >= boxes[i - 1].Right + graph.NodeSpacing - 0.01);
        }
    }

    [Fact]
    public void ClustersContainTheirMembers()
    {
        var graph = new LayeredGraph();
        graph.AddCluster("outer", null, 60, 16);
        graph.AddCluster("inner", "outer", 40, 16);
        graph.AddNode("a", 40, 20, "inner");
        graph.AddNode("b", 40, 20, "outer");
        graph.AddNode("c", 40, 20);
        graph.AddEdge("a", "b");
        graph.AddEdge("b", "c");
        graph.AddEdge("c", "a");
        var layout = LayeredLayout.Compute(graph);
        Contains(layout.Clusters["inner"].Bounds, layout.Nodes["a"].Bounds);
        Contains(layout.Clusters["outer"].Bounds, layout.Clusters["inner"].Bounds);
        Contains(layout.Clusters["outer"].Bounds, layout.Nodes["b"].Bounds);
        Assert.False(Overlaps(layout.Clusters["outer"].Bounds, layout.Nodes["c"].Bounds));
    }

    [Fact]
    public void EdgesRunBetweenNodeCenters()
    {
        var layout = LayeredLayout.Compute(Chain(LayoutDirection.TopToBottom));
        foreach (var edge in layout.Edges)
        {
            var from = layout.Nodes[edge.From].Bounds;
            var to = layout.Nodes[edge.To].Bounds;
            Assert.Equal(new Point(from.CenterX, from.CenterY), edge.Points[0]);
            Assert.Equal(new Point(to.CenterX, to.CenterY), edge.Points[^1]);
        }
    }

    [Fact]
    public void CyclesAndSelfLoopsAreLaidOut()
    {
        var graph = Chain(LayoutDirection.TopToBottom);
        graph.AddEdge("c", "a");
        graph.AddEdge("b", "b");
        var layout = LayeredLayout.Compute(graph);
        Assert.Equal(4, layout.Edges.Count);
        Assert.All(layout.Edges, e => Assert.True(e.Points.Count >= 2));
    }

    [Fact]
    public void InvalidSizesAreClamped()
    {
        var graph = new LayeredGraph { NodeSpacing = -50, RankSpacing = double.PositiveInfinity, EdgeSpacing = double.NaN };
        graph.AddNode("a", double.NaN, -10);
        graph.AddNode("b", double.PositiveInfinity, 20);
        graph.AddEdge("a", "b", double.NaN, -1);
        Assert.Equal(0, graph.NodeSpacing);
        Assert.Equal(0, graph.EdgeSpacing);
        var layout = LayeredLayout.Compute(graph);
        Assert.True(double.IsFinite(layout.Width) && double.IsFinite(layout.Height));
    }

    [Fact]
    public void DuplicateIdsAreRejected()
    {
        var graph = new LayeredGraph();
        graph.AddNode("a", 1, 1);
        Assert.Throws<ArgumentException>(() => graph.AddNode("a", 1, 1));
        Assert.Throws<ArgumentException>(() => graph.AddCluster("a"));
        Assert.Throws<ArgumentException>(() => graph.AddEdge("a", "missing"));
    }

    private static void Contains(Rect outer, Rect inner) =>
        Assert.True(inner.X >= outer.X - 0.01 && inner.Y >= outer.Y - 0.01 && inner.Right <= outer.Right + 0.01 && inner.Bottom <= outer.Bottom + 0.01, $"{outer} does not contain {inner}");

    private static bool Overlaps(Rect a, Rect b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;
}
