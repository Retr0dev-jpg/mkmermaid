namespace MkMermaid.Layout;

/// <summary>
/// Layered graph layout with clusters. A cluster whose members have no edges to the outside is laid out on its
/// own (in its own direction, if set) and placed as a single box; other clusters are kept contiguous in the
/// enclosing layout.
/// </summary>
public static class LayeredLayout
{
    private const int MaxClusterDepth = 64;

    public static LayeredResult Compute(LayeredGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var model = new Model(graph);
        var scope = model.LayoutScope(-1, graph.Direction, 0);
        scope.Normalize();
        return new LayeredResult(
            scope.Width,
            scope.Height,
            scope.Nodes.ToDictionary(p => p.Key, p => new LayeredNode(p.Key, p.Value), StringComparer.Ordinal),
            Enumerable.Range(0, graph.Edges.Count)
                .Select(i => scope.Edges.TryGetValue(i, out var e)
                    ? new LayeredEdge(i, graph.Edges[i].From, graph.Edges[i].To, e.Points, e.Label)
                    : new LayeredEdge(i, graph.Edges[i].From, graph.Edges[i].To, [], null))
                .ToList(),
            scope.Clusters.ToDictionary(p => p.Key, p => new LayeredCluster(p.Key, p.Value.Bounds, p.Value.Label), StringComparer.Ordinal));
    }

    /// <summary>Point where the segment from <paramref name="inside"/> toward <paramref name="outside"/> leaves the rectangle.</summary>
    public static Point IntersectRect(Rect rect, Point inside, Point outside)
    {
        var dx = outside.X - inside.X;
        var dy = outside.Y - inside.Y;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9)
        {
            return inside;
        }

        var t = double.PositiveInfinity;
        if (dx > 0)
        {
            t = Math.Min(t, (rect.Right - inside.X) / dx);
        }
        else if (dx < 0)
        {
            t = Math.Min(t, (rect.X - inside.X) / dx);
        }

        if (dy > 0)
        {
            t = Math.Min(t, (rect.Bottom - inside.Y) / dy);
        }
        else if (dy < 0)
        {
            t = Math.Min(t, (rect.Y - inside.Y) / dy);
        }

        t = Math.Clamp(t, 0, 1);
        return new Point(inside.X + dx * t, inside.Y + dy * t);
    }

    /// <summary>Clips the ends of an edge route to the given boxes (ends outside their box are left unchanged).</summary>
    public static IReadOnlyList<Point> Clip(IReadOnlyList<Point> points, Rect from, Rect to)
    {
        if (points.Count < 2)
        {
            return points;
        }

        var result = points.ToList();
        if (from.Contains(result[0]))
        {
            var next = result.Skip(1).FirstOrDefault(p => !from.Contains(p), result[^1]);
            result[0] = IntersectRect(from, result[0], next);
            while (result.Count > 2 && from.Contains(result[1]))
            {
                result.RemoveAt(1);
            }
        }

        if (to.Contains(result[^1]))
        {
            var previous = Enumerable.Reverse(result).Skip(1).FirstOrDefault(p => !to.Contains(p), result[0]);
            result[^1] = IntersectRect(to, result[^1], previous);
            while (result.Count > 2 && to.Contains(result[^2]))
            {
                result.RemoveAt(result.Count - 2);
            }
        }

        return result;
    }

    private sealed class Scope
    {
        public Dictionary<string, Rect> Nodes { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, (Rect Bounds, Rect Label)> Clusters { get; } = new(StringComparer.Ordinal);

        public Dictionary<int, (List<Point> Points, Point? Label)> Edges { get; } = [];

        /// <summary>Edge label boxes, which count toward the extent.</summary>
        public List<Rect> Labels { get; } = [];

        public double Width { get; private set; }

        public double Height { get; private set; }

        public void Translate(double dx, double dy, Scope target)
        {
            foreach (var (id, r) in Nodes)
            {
                target.Nodes[id] = r with { X = r.X + dx, Y = r.Y + dy };
            }

            foreach (var (id, (b, l)) in Clusters)
            {
                target.Clusters[id] = (b with { X = b.X + dx, Y = b.Y + dy }, l with { X = l.X + dx, Y = l.Y + dy });
            }

            foreach (var (index, (points, label)) in Edges)
            {
                target.Edges[index] = (points.Select(p => new Point(p.X + dx, p.Y + dy)).ToList(), label is { } c ? new Point(c.X + dx, c.Y + dy) : null);
            }

            target.Labels.AddRange(Labels.Select(r => r with { X = r.X + dx, Y = r.Y + dy }));
        }

        public void Normalize()
        {
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            void Add(double x, double y)
            {
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }

            foreach (var r in Nodes.Values)
            {
                Add(r.X, r.Y);
                Add(r.Right, r.Bottom);
            }

            foreach (var (b, _) in Clusters.Values)
            {
                if (b.Width > 0 || b.Height > 0)
                {
                    Add(b.X, b.Y);
                    Add(b.Right, b.Bottom);
                }
            }

            foreach (var (points, _) in Edges.Values)
            {
                foreach (var p in points)
                {
                    Add(p.X, p.Y);
                }
            }

            foreach (var r in Labels)
            {
                Add(r.X, r.Y);
                Add(r.Right, r.Bottom);
            }

            if (double.IsInfinity(minX))
            {
                Width = Height = 0;
                return;
            }

            var shifted = new Scope();
            Translate(-minX, -minY, shifted);
            Nodes.Clear();
            Clusters.Clear();
            Edges.Clear();
            Labels.Clear();
            shifted.Translate(0, 0, this);
            Width = maxX - minX;
            Height = maxY - minY;
        }
    }

    private sealed class Model
    {
        private readonly LayeredGraph _graph;
        private readonly int[] _clusterParent;
        private readonly int[] _nodeCluster;
        private readonly List<int>[] _childClusters;
        private readonly List<int>[] _childNodes;

        public Model(LayeredGraph graph)
        {
            _graph = graph;
            var clusterCount = graph.Clusters.Count;
            _clusterParent = new int[clusterCount];
            for (var c = 0; c < clusterCount; c++)
            {
                var parent = graph.Clusters[c].Parent;
                _clusterParent[c] = parent is not null && graph.ClusterIndex.TryGetValue(parent, out var p) && p != c ? p : -1;
            }

            // Break parent cycles and excessive nesting.
            for (var c = 0; c < clusterCount; c++)
            {
                var depth = 0;
                for (var k = _clusterParent[c]; k >= 0; k = _clusterParent[k])
                {
                    if (k == c || ++depth > MaxClusterDepth)
                    {
                        _clusterParent[c] = -1;
                        break;
                    }
                }
            }

            _nodeCluster = graph.Nodes.Select(n => n.Cluster is not null && graph.ClusterIndex.TryGetValue(n.Cluster, out var c) ? c : -1).ToArray();
            _childClusters = new List<int>[clusterCount + 1];
            _childNodes = new List<int>[clusterCount + 1];
            for (var i = 0; i <= clusterCount; i++)
            {
                _childClusters[i] = [];
                _childNodes[i] = [];
            }

            for (var c = 0; c < clusterCount; c++)
            {
                _childClusters[_clusterParent[c] + 1].Add(c);
            }

            for (var n = 0; n < graph.Nodes.Count; n++)
            {
                _childNodes[_nodeCluster[n] + 1].Add(n);
            }
        }

        private bool ClusterInside(int cluster, int ancestor)
        {
            for (var k = cluster; k >= 0; k = _clusterParent[k])
            {
                if (k == ancestor)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when the endpoint lies strictly inside <paramref name="cluster"/> (-1 is the root).</summary>
        private bool Inside(string id, int cluster)
        {
            if (cluster < 0)
            {
                return true;
            }

            if (_graph.NodeIndex.TryGetValue(id, out var n))
            {
                return _nodeCluster[n] >= 0 && ClusterInside(_nodeCluster[n], cluster);
            }

            var c = _graph.ClusterIndex[id];
            return c != cluster && ClusterInside(c, cluster);
        }

        private bool HasMembers(int cluster) =>
            _childNodes[cluster + 1].Count > 0 || _childClusters[cluster + 1].Any(HasMembers);

        private bool Collapsible(int cluster)
        {
            if (!HasMembers(cluster))
            {
                return false;
            }

            foreach (var e in _graph.Edges)
            {
                if (Inside(e.From, cluster) != Inside(e.To, cluster))
                {
                    var outsideEnd = Inside(e.From, cluster) ? e.To : e.From;
                    var insideEnd = Inside(e.From, cluster) ? e.From : e.To;
                    if (outsideEnd != _graph.Clusters[cluster].Id || insideEnd == outsideEnd)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public Scope LayoutScope(int scopeCluster, LayoutDirection direction, int depth)
        {
            var items = new List<Sugiyama.Item>();
            var itemIds = new List<string>();
            var itemOf = new Dictionary<string, int>(StringComparer.Ordinal);
            var clusters = new List<Sugiyama.Cluster>();
            var clusterIds = new List<int>();
            var collapsed = new Dictionary<int, (Scope Inner, double OffsetX, double OffsetY)>();
            var representative = new Dictionary<string, int>(StringComparer.Ordinal);

            void AddItems(int cluster, int flatParent)
            {
                foreach (var n in _childNodes[cluster + 1])
                {
                    var spec = _graph.Nodes[n];
                    itemOf[spec.Id] = items.Count;
                    itemIds.Add(spec.Id);
                    items.Add(new Sugiyama.Item(spec.Width, spec.Height, flatParent));
                }

                foreach (var c in _childClusters[cluster + 1])
                {
                    var spec = _graph.Clusters[c];
                    if (!HasMembers(c))
                    {
                        continue;
                    }

                    if (depth < MaxClusterDepth && Collapsible(c))
                    {
                        var inner = LayoutScope(c, spec.Direction ?? direction, depth + 1);
                        var pad = spec.Padding;
                        var labelSpace = spec.LabelHeight > 0 ? spec.LabelHeight + pad / 2 : 0;
                        var width = Math.Max(inner.Width, spec.LabelWidth) + 2 * pad;
                        var height = inner.Height + 2 * pad + labelSpace;
                        collapsed[items.Count] = (inner, pad + (width - 2 * pad - inner.Width) / 2, pad + labelSpace);
                        itemOf[spec.Id] = items.Count;
                        itemIds.Add(spec.Id);
                        items.Add(new Sugiyama.Item(width, height, flatParent));
                        MarkRepresentatives(c, items.Count - 1, representative);
                    }
                    else
                    {
                        var flat = clusters.Count;
                        clusters.Add(new Sugiyama.Cluster(flatParent, spec.LabelWidth, spec.LabelHeight, spec.Padding));
                        clusterIds.Add(c);
                        AddItems(c, flat);
                    }
                }
            }

            AddItems(scopeCluster, -1);

            int Resolve(string id)
            {
                if (itemOf.TryGetValue(id, out var item))
                {
                    return item;
                }

                if (representative.TryGetValue(id, out var placeholder))
                {
                    return placeholder;
                }

                // Edge to an expanded cluster: anchor it to the cluster's first member.
                return _graph.ClusterIndex.TryGetValue(id, out var c) ? Anchor(c, itemOf, representative) : -1;
            }

            var edges = new List<Sugiyama.Edge>();
            for (var i = 0; i < _graph.Edges.Count; i++)
            {
                var e = _graph.Edges[i];
                if (!Inside(e.From, scopeCluster) || !Inside(e.To, scopeCluster))
                {
                    continue;
                }

                var from = Resolve(e.From);
                var to = Resolve(e.To);
                if (from < 0 || to < 0)
                {
                    continue;
                }

                // Edges entirely within a collapsed cluster belong to its inner layout.
                if (from == to && collapsed.ContainsKey(from) && itemIds[from] != e.From && itemIds[from] != e.To)
                {
                    continue;
                }

                edges.Add(new Sugiyama.Edge(from, to, e.LabelWidth, e.LabelHeight, e.MinLength, i));
            }

            var output = Sugiyama.Run(items, [.. clusters], edges, direction, _graph.NodeSpacing, _graph.RankSpacing, _graph.EdgeSpacing);
            var scope = new Scope();
            for (var i = 0; i < items.Count; i++)
            {
                var center = output.Centers[i];
                var rect = new Rect(center.X - items[i].Width / 2, center.Y - items[i].Height / 2, items[i].Width, items[i].Height);
                if (collapsed.TryGetValue(i, out var inner))
                {
                    var spec = _graph.Clusters[_graph.ClusterIndex[itemIds[i]]];
                    var labelRect = new Rect(rect.CenterX - spec.LabelWidth / 2, rect.Y + spec.Padding / 2, spec.LabelWidth, spec.LabelHeight);
                    scope.Clusters[itemIds[i]] = (rect, labelRect);
                    inner.Inner.Translate(rect.X + inner.OffsetX, rect.Y + inner.OffsetY, scope);
                }
                else
                {
                    scope.Nodes[itemIds[i]] = rect;
                }
            }

            for (var k = 0; k < clusterIds.Count; k++)
            {
                scope.Clusters[_graph.Clusters[clusterIds[k]].Id] = (output.ClusterBounds[k], output.ClusterLabels[k]);
            }

            foreach (var (index, value) in output.Edges)
            {
                scope.Edges[index] = value;
                if (value.Label is { } center)
                {
                    var spec = _graph.Edges[index];
                    scope.Labels.Add(new Rect(center.X - spec.LabelWidth / 2, center.Y - spec.LabelHeight / 2, spec.LabelWidth, spec.LabelHeight));
                }
            }

            scope.Normalize();
            return scope;
        }

        private void MarkRepresentatives(int cluster, int item, Dictionary<string, int> representative)
        {
            foreach (var n in _childNodes[cluster + 1])
            {
                representative[_graph.Nodes[n].Id] = item;
            }

            foreach (var c in _childClusters[cluster + 1])
            {
                representative[_graph.Clusters[c].Id] = item;
                MarkRepresentatives(c, item, representative);
            }
        }

        private int Anchor(int cluster, Dictionary<string, int> itemOf, Dictionary<string, int> representative)
        {
            foreach (var n in _childNodes[cluster + 1])
            {
                if (itemOf.TryGetValue(_graph.Nodes[n].Id, out var item))
                {
                    return item;
                }
            }

            foreach (var c in _childClusters[cluster + 1])
            {
                if (itemOf.TryGetValue(_graph.Clusters[c].Id, out var item) || representative.TryGetValue(_graph.Clusters[c].Id, out item))
                {
                    return item;
                }

                var nested = Anchor(c, itemOf, representative);
                if (nested >= 0)
                {
                    return nested;
                }
            }

            return -1;
        }
    }
}
