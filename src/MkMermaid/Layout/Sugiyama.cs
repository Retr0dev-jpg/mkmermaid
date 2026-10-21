namespace MkMermaid.Layout;

/// <summary>
/// Layered (Sugiyama) layout of one flat scope. Works internally top-to-bottom and transforms the result
/// for the other directions. Edge minimum lengths are doubled so every edge has a middle rank for its label.
/// </summary>
internal sealed class Sugiyama
{
    public sealed record Item(double Width, double Height, int Cluster);

    public sealed record Cluster(int Parent, double LabelWidth, double LabelHeight, double Padding);

    public sealed record Edge(int From, int To, double LabelWidth, double LabelHeight, int MinLength, int Id);

    /// <summary>Layout result; <see cref="Edges"/> is keyed by <see cref="Edge.Id"/>.</summary>
    public sealed record Output(
        Point[] Centers,
        Dictionary<int, (List<Point> Points, Point? Label)> Edges,
        Rect[] ClusterBounds,
        Rect[] ClusterLabels);

    private const int MaxOrderIterations = 24;
    private const int MaxRankPasses = 12;

    private readonly List<double> _w = [];
    private readonly List<double> _h = [];
    private readonly List<int> _owner = [];
    private readonly List<bool> _dummy = [];
    private readonly List<int> _rank = [];
    private readonly List<List<(int Node, double Weight)>> _up = [];
    private readonly List<List<(int Node, double Weight)>> _down = [];
    private readonly Cluster[] _clusters;
    private readonly int[][] _clusterPath;
    private readonly int[] _clusterDepth;
    private readonly bool _horizontal;
    private readonly LayoutDirection _direction;
    private readonly double _nodeSep;
    private readonly double _edgeSep;
    private readonly double _rankSep;
    private int[][] _nodePath = [];
    private List<int>[] _layers = [];
    private int[] _pos = [];
    private double[] _x = [];
    private double[] _y = [];
    private double[] _extra = [];

    private Sugiyama(Cluster[] clusters, LayoutDirection direction, double nodeSep, double rankSep, double edgeSep)
    {
        _clusters = clusters;
        _direction = direction;
        _horizontal = direction is LayoutDirection.LeftToRight or LayoutDirection.RightToLeft;
        _nodeSep = nodeSep;
        _rankSep = rankSep;
        _edgeSep = edgeSep;
        _clusterPath = new int[clusters.Length][];
        _clusterDepth = new int[clusters.Length];
        for (var c = 0; c < clusters.Length; c++)
        {
            var path = new List<int>();
            for (var k = c; k >= 0 && path.Count <= clusters.Length; k = clusters[k].Parent)
            {
                path.Add(k);
            }

            path.Reverse();
            _clusterPath[c] = [.. path];
            _clusterDepth[c] = path.Count;
        }
    }

    public static Output Run(IReadOnlyList<Item> items, Cluster[] clusters, IReadOnlyList<Edge> edges, LayoutDirection direction, double nodeSep, double rankSep, double edgeSep) =>
        new Sugiyama(clusters, direction, nodeSep, rankSep, edgeSep).Layout(items, edges);

    private int AddNode(double w, double h, int cluster, bool dummy)
    {
        _w.Add(w);
        _h.Add(h);
        _owner.Add(cluster);
        _dummy.Add(dummy);
        _rank.Add(0);
        _up.Add([]);
        _down.Add([]);
        return _w.Count - 1;
    }

    private Output Layout(IReadOnlyList<Item> items, IReadOnlyList<Edge> edges)
    {
        var realCount = items.Count;
        foreach (var item in items)
        {
            var (w, h) = _horizontal ? (item.Height, item.Width) : (item.Width, item.Height);
            AddNode(w, h, item.Cluster, false);
        }

        // Work edges: (u, v, minlen, edge index); self-loops are drawn separately.
        var work = new List<(int U, int V, int MinLen, int Index, bool Reversed)>();
        var selfLoops = new List<int>();
        for (var i = 0; i < edges.Count; i++)
        {
            var e = edges[i];
            if (e.From == e.To)
            {
                selfLoops.Add(i);
                continue;
            }

            work.Add((e.From, e.To, e.MinLength * 2, i, false));
        }

        _extra = new double[realCount];
        foreach (var i in selfLoops)
        {
            var e = edges[i];
            _extra[e.From] = Math.Max(_extra[e.From], 40 + (_horizontal ? e.LabelHeight : e.LabelWidth));
        }

        BreakCycles(realCount, work);
        AssignRanks(realCount, work);

        // Split long edges with dummy nodes; the middle dummy carries the label.
        var chains = new List<int>[work.Count];
        for (var i = 0; i < work.Count; i++)
        {
            var (u, v, _, index, _) = work[i];
            var e = edges[index];
            var span = _rank[v] - _rank[u];
            var chain = new List<int> { u };
            var owner = CommonCluster(_owner[u], _owner[v]);
            var labelRank = _rank[u] + span / 2;
            var (lw, lh) = _horizontal ? (e.LabelHeight, e.LabelWidth) : (e.LabelWidth, e.LabelHeight);
            for (var r = _rank[u] + 1; r < _rank[v]; r++)
            {
                var isLabel = r == labelRank && (lw > 0 || lh > 0);
                var d = AddNode(isLabel ? lw : 0, isLabel ? lh : 0, owner, true);
                _rank[d] = r;
                chain.Add(d);
            }

            chain.Add(v);
            for (var k = 0; k + 1 < chain.Count; k++)
            {
                var a = chain[k];
                var b = chain[k + 1];
                var weight = _dummy[a] && _dummy[b] ? 8.0 : _dummy[a] || _dummy[b] ? 2.0 : 1.0;
                _down[a].Add((b, weight));
                _up[b].Add((a, weight));
            }

            chains[i] = chain;
        }

        var count = _w.Count;
        _nodePath = new int[count][];
        for (var n = 0; n < count; n++)
        {
            _nodePath[n] = _owner[n] >= 0 ? _clusterPath[_owner[n]] : [];
        }

        BuildLayers(count);
        Order();
        AssignX();
        PushOutOfClusters();
        AssignY();

        var (clusterBounds, clusterLabels) = ClusterRects();
        var output = new Output(new Point[realCount], [], clusterBounds, clusterLabels);
        for (var n = 0; n < realCount; n++)
        {
            output.Centers[n] = Transform(_x[n], _y[n]);
        }

        for (var i = 0; i < work.Count; i++)
        {
            var chain = chains[i];
            var points = chain.Select(n => Transform(_x[n], _y[n])).ToList();
            Point? label = null;
            var e = edges[work[i].Index];
            if (e.LabelWidth > 0 || e.LabelHeight > 0)
            {
                var labelNode = chain.Skip(1).Take(chain.Count - 2).FirstOrDefault(n => _w[n] > 0 || _h[n] > 0, -1);
                label = labelNode >= 0 ? Transform(_x[labelNode], _y[labelNode]) : Mid(points);
            }

            if (work[i].Reversed)
            {
                points.Reverse();
            }

            output.Edges[e.Id] = (points, label);
        }

        foreach (var i in selfLoops)
        {
            output.Edges[edges[i].Id] = SelfLoop(items[edges[i].From], output.Centers[edges[i].From], edges[i]);
        }

        for (var c = 0; c < _clusters.Length; c++)
        {
            output.ClusterBounds[c] = TransformRect(output.ClusterBounds[c]);
            output.ClusterLabels[c] = TransformRect(output.ClusterLabels[c]);
        }

        return output;
    }

    private static Point Mid(List<Point> points) =>
        points.Count == 0 ? default : new Point((points[0].X + points[^1].X) / 2, (points[0].Y + points[^1].Y) / 2);

    private (List<Point>, Point?) SelfLoop(Item item, Point center, Edge edge)
    {
        if (_horizontal)
        {
            var bottom = center.Y + item.Height / 2;
            var points = new List<Point>
            {
                new(center.X - item.Width / 4, bottom),
                new(center.X - item.Width / 4 - 10, bottom + 30),
                new(center.X + item.Width / 4 + 10, bottom + 30),
                new(center.X + item.Width / 4, bottom)
            };
            Point? label = edge.LabelWidth > 0 ? new Point(center.X, bottom + 34 + edge.LabelHeight / 2) : null;
            return (points, label);
        }
        else
        {
            var right = center.X + item.Width / 2;
            var points = new List<Point>
            {
                new(right, center.Y - item.Height / 4),
                new(right + 30, center.Y - item.Height / 4 - 10),
                new(right + 30, center.Y + item.Height / 4 + 10),
                new(right, center.Y + item.Height / 4)
            };
            Point? label = edge.LabelWidth > 0 ? new Point(right + 34 + edge.LabelWidth / 2, center.Y) : null;
            return (points, label);
        }
    }

    private Point Transform(double x, double y)
    {
        var (fx, fy) = _horizontal ? (y, x) : (x, y);
        return _direction switch
        {
            LayoutDirection.BottomToTop => new Point(fx, -fy),
            LayoutDirection.RightToLeft => new Point(-fx, fy),
            _ => new Point(fx, fy)
        };
    }

    private Rect TransformRect(Rect r)
    {
        var a = Transform(r.X, r.Y);
        var b = Transform(r.Right, r.Bottom);
        return new Rect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
    }

    private int CommonCluster(int a, int b)
    {
        if (a < 0 || b < 0)
        {
            return -1;
        }

        var pa = _clusterPath[a];
        var pb = _clusterPath[b];
        var common = -1;
        for (var i = 0; i < Math.Min(pa.Length, pb.Length) && pa[i] == pb[i]; i++)
        {
            common = pa[i];
        }

        return common;
    }

    private static void BreakCycles(int count, List<(int U, int V, int MinLen, int Index, bool Reversed)> work)
    {
        var outgoing = new List<int>[count];
        for (var n = 0; n < count; n++)
        {
            outgoing[n] = [];
        }

        for (var i = 0; i < work.Count; i++)
        {
            outgoing[work[i].U].Add(i);
        }

        var state = new byte[count];
        var stack = new Stack<(int Node, int Next)>();
        for (var start = 0; start < count; start++)
        {
            if (state[start] != 0)
            {
                continue;
            }

            stack.Push((start, 0));
            state[start] = 1;
            while (stack.Count > 0)
            {
                var (node, next) = stack.Pop();
                if (next < outgoing[node].Count)
                {
                    stack.Push((node, next + 1));
                    var edge = outgoing[node][next];
                    var target = work[edge].V;
                    if (state[target] == 1)
                    {
                        var w = work[edge];
                        work[edge] = (w.V, w.U, w.MinLen, w.Index, true);
                    }
                    else if (state[target] == 0)
                    {
                        state[target] = 1;
                        stack.Push((target, 0));
                    }
                }
                else
                {
                    state[node] = 2;
                }
            }
        }
    }

    private void AssignRanks(int count, List<(int U, int V, int MinLen, int Index, bool Reversed)> work)
    {
        var ins = new List<(int Node, int MinLen)>[count];
        var outs = new List<(int Node, int MinLen)>[count];
        for (var n = 0; n < count; n++)
        {
            ins[n] = [];
            outs[n] = [];
        }

        foreach (var (u, v, minLen, _, _) in work)
        {
            outs[u].Add((v, minLen));
            ins[v].Add((u, minLen));
        }

        var indegree = ins.Select(l => l.Count).ToArray();
        var queue = new Queue<int>(Enumerable.Range(0, count).Where(n => indegree[n] == 0));
        var topo = new List<int>(count);
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            topo.Add(n);
            foreach (var (v, minLen) in outs[n])
            {
                _rank[v] = Math.Max(_rank[v], _rank[n] + minLen);
                if (--indegree[v] == 0)
                {
                    queue.Enqueue(v);
                }
            }
        }

        // Balance: move each node within its feasible range toward the side with more edge weight,
        // which shortens edges much like network simplex does for these graphs.
        for (var pass = 0; pass < MaxRankPasses; pass++)
        {
            var changed = false;
            for (var k = topo.Count - 1; k >= 0; k--)
            {
                var n = topo[k];
                var lo = ins[n].Count == 0 ? int.MinValue : ins[n].Max(e => _rank[e.Node] + e.MinLen);
                var hi = outs[n].Count == 0 ? int.MaxValue : outs[n].Min(e => _rank[e.Node] - e.MinLen);
                if (lo == int.MinValue && hi == int.MaxValue)
                {
                    continue;
                }

                int target;
                if (ins[n].Count == 0)
                {
                    target = hi;
                }
                else if (outs[n].Count == 0)
                {
                    target = lo;
                }
                else if (ins[n].Count > outs[n].Count)
                {
                    target = lo;
                }
                else if (outs[n].Count > ins[n].Count)
                {
                    target = hi;
                }
                else
                {
                    target = Math.Clamp(_rank[n], lo, hi);
                }

                if (target != _rank[n] && target >= lo && target <= hi)
                {
                    _rank[n] = target;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        var min = count == 0 ? 0 : Enumerable.Range(0, count).Min(n => _rank[n]);
        for (var n = 0; n < count; n++)
        {
            _rank[n] -= min;
        }
    }

    private void BuildLayers(int count)
    {
        var maxRank = count == 0 ? 0 : _rank.Max();
        _layers = new List<int>[maxRank + 1];
        for (var r = 0; r <= maxRank; r++)
        {
            _layers[r] = [];
        }

        // Initial order: depth-first from the nodes in input order (dagre's initOrder).
        var visited = new bool[count];
        var starts = Enumerable.Range(0, count).OrderBy(n => _rank[n]).ThenBy(n => n).ToList();
        var stack = new Stack<int>();
        foreach (var start in starts)
        {
            if (visited[start])
            {
                continue;
            }

            stack.Push(start);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (visited[n])
                {
                    continue;
                }

                visited[n] = true;
                _layers[_rank[n]].Add(n);
                for (var i = _down[n].Count - 1; i >= 0; i--)
                {
                    if (!visited[_down[n][i].Node])
                    {
                        stack.Push(_down[n][i].Node);
                    }
                }
            }
        }

        _pos = new int[count];
        _x = new double[count];
        _y = new double[count];
        var keys = GlobalClusterKeys();
        foreach (var layer in _layers)
        {
            var bary = new Dictionary<int, double>();
            for (var i = 0; i < layer.Count; i++)
            {
                bary[layer[i]] = i;
            }

            var sorted = SortGroup(layer, bary, keys, 0);
            layer.Clear();
            layer.AddRange(sorted);
        }

        UpdatePositions();
    }

    private void UpdatePositions()
    {
        foreach (var layer in _layers)
        {
            for (var i = 0; i < layer.Count; i++)
            {
                _pos[layer[i]] = i;
            }
        }
    }

    private void Order()
    {
        if (_layers.Length < 2)
        {
            return;
        }

        var best = _layers.Select(l => l.ToList()).ToArray();
        var bestCrossings = Crossings();
        var stale = 0;
        for (var iteration = 0; iteration < MaxOrderIterations && bestCrossings > 0; iteration++)
        {
            Sweep(iteration % 2 == 0);
            var crossings = Crossings();
            if (crossings < bestCrossings)
            {
                bestCrossings = crossings;
                best = _layers.Select(l => l.ToList()).ToArray();
                stale = 0;
            }
            else if (++stale >= 6)
            {
                break;
            }
        }

        _layers = best.Select(l => l.ToList()).ToArray();
        UpdatePositions();
    }

    private void Sweep(bool down)
    {
        var keys = GlobalClusterKeys();
        var range = down ? Enumerable.Range(1, _layers.Length - 1) : Enumerable.Range(0, _layers.Length - 1).Reverse();
        foreach (var r in range)
        {
            var layer = _layers[r];
            var bary = new Dictionary<int, double>();
            foreach (var n in layer)
            {
                var neighbors = down ? _up[n] : _down[n];
                if (neighbors.Count == 0)
                {
                    bary[n] = _pos[n];
                    continue;
                }

                double sum = 0, weight = 0;
                foreach (var (m, w) in neighbors)
                {
                    sum += _pos[m] * w;
                    weight += w;
                }

                bary[n] = sum / weight;
            }

            var sorted = SortGroup(layer, bary, keys, 0);
            layer.Clear();
            layer.AddRange(sorted);
            for (var i = 0; i < layer.Count; i++)
            {
                _pos[layer[i]] = i;
            }
        }
    }

    private double[] GlobalClusterKeys()
    {
        var sum = new double[_clusters.Length];
        var count = new int[_clusters.Length];
        foreach (var layer in _layers)
        {
            for (var i = 0; i < layer.Count; i++)
            {
                var position = layer.Count == 1 ? 0.5 : (double)i / (layer.Count - 1);
                foreach (var c in _nodePath[layer[i]])
                {
                    sum[c] += position;
                    count[c]++;
                }
            }
        }

        return sum.Select((s, c) => count[c] == 0 ? 0.5 : s / count[c]).ToArray();
    }

    /// <summary>Sorts a layer by barycenter while keeping every cluster contiguous and sibling clusters in a global order.</summary>
    private List<int> SortGroup(List<int> nodes, Dictionary<int, double> bary, double[] keys, int depth)
    {
        var units = new List<(int Cluster, int Node, double Key, int First)>();
        var groups = new Dictionary<int, List<int>>();
        for (var i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            var path = _nodePath[n];
            if (path.Length > depth)
            {
                var c = path[depth];
                if (!groups.TryGetValue(c, out var list))
                {
                    groups[c] = list = [];
                    units.Add((c, -1, 0, i));
                }

                list.Add(n);
            }
            else
            {
                units.Add((-1, n, bary[n], i));
            }
        }

        for (var i = 0; i < units.Count; i++)
        {
            if (units[i].Cluster >= 0)
            {
                units[i] = units[i] with { Key = groups[units[i].Cluster].Average(n => bary[n]) };
            }
        }

        var sorted = units.OrderBy(u => u.Key).ThenBy(u => u.First).ToList();
        var clusterSlots = sorted.Select((u, i) => (u, i)).Where(p => p.u.Cluster >= 0).Select(p => p.i).ToList();
        if (clusterSlots.Count > 1)
        {
            var clusterUnits = clusterSlots.Select(i => sorted[i]).OrderBy(u => keys[u.Cluster]).ThenBy(u => u.Key).ToList();
            for (var k = 0; k < clusterSlots.Count; k++)
            {
                sorted[clusterSlots[k]] = clusterUnits[k];
            }
        }

        var result = new List<int>(nodes.Count);
        foreach (var unit in sorted)
        {
            if (unit.Cluster < 0)
            {
                result.Add(unit.Node);
            }
            else
            {
                result.AddRange(SortGroup(groups[unit.Cluster], bary, keys, depth + 1));
            }
        }

        return result;
    }

    private double Crossings()
    {
        double total = 0;
        for (var r = 0; r + 1 < _layers.Length; r++)
        {
            var pairs = new List<(int A, int B, double W)>();
            foreach (var n in _layers[r])
            {
                foreach (var (m, w) in _down[n])
                {
                    pairs.Add((_pos[n], _pos[m], w));
                }
            }

            pairs.Sort((p, q) => p.A != q.A ? p.A.CompareTo(q.A) : p.B.CompareTo(q.B));
            var size = _layers[r + 1].Count + 1;
            var tree = new double[size + 1];
            double seen = 0;
            foreach (var (_, b, w) in pairs)
            {
                // Weight of already inserted edges ending to the right of b crosses this edge.
                double atOrBelow = 0;
                for (var i = b + 1; i > 0; i -= i & -i)
                {
                    atOrBelow += tree[i];
                }

                total += (seen - atOrBelow) * w;
                for (var i = b + 1; i <= size; i += i & -i)
                {
                    tree[i] += w;
                }

                seen += w;
            }
        }

        return total;
    }

    private double Width(int n) => _w[n] + (n < _extra.Length ? 2 * _extra[n] : 0);

    private double Separation(int a, int b)
    {
        var gap = (_dummy[a] ? _edgeSep : _nodeSep) / 2 + (_dummy[b] ? _edgeSep : _nodeSep) / 2;
        var pa = _nodePath[a];
        var pb = _nodePath[b];
        var common = 0;
        while (common < pa.Length && common < pb.Length && pa[common] == pb[common])
        {
            common++;
        }

        for (var i = common; i < pa.Length; i++)
        {
            gap += _clusters[pa[i]].Padding;
        }

        for (var i = common; i < pb.Length; i++)
        {
            gap += _clusters[pb[i]].Padding + LabelSide(pb[i]);
        }

        return Width(a) / 2 + Width(b) / 2 + gap;
    }

    /// <summary>Space a cluster label takes on the internal left side (the final top in left-to-right layouts).</summary>
    private double LabelSide(int c) => _horizontal && _clusters[c].LabelHeight > 0 ? _clusters[c].LabelHeight + _clusters[c].Padding / 2 : 0;

    private void AssignX()
    {
        foreach (var layer in _layers)
        {
            for (var i = 0; i < layer.Count; i++)
            {
                _x[layer[i]] = i == 0 ? 0 : _x[layer[i - 1]] + Separation(layer[i - 1], layer[i]);
            }
        }

        for (var iteration = 0; iteration < 8; iteration++)
        {
            for (var r = 1; r < _layers.Length; r++)
            {
                PlaceLayer(_layers[r], true, false);
            }

            for (var r = _layers.Length - 2; r >= 0; r--)
            {
                PlaceLayer(_layers[r], false, true);
            }
        }

        for (var iteration = 0; iteration < 2; iteration++)
        {
            foreach (var layer in _layers)
            {
                PlaceLayer(layer, true, true);
            }
        }
    }

    /// <summary>Moves a layer's nodes as close as possible to the mean of their neighbors (weighted isotonic regression).</summary>
    private void PlaceLayer(List<int> layer, bool useUp, bool useDown)
    {
        var k = layer.Count;
        if (k == 0)
        {
            return;
        }

        var offset = new double[k];
        var target = new double[k];
        var weight = new double[k];
        for (var i = 0; i < k; i++)
        {
            var n = layer[i];
            offset[i] = i == 0 ? 0 : offset[i - 1] + Separation(layer[i - 1], n);
            double sum = 0, w = 0;
            if (useUp)
            {
                foreach (var (m, ew) in _up[n])
                {
                    sum += _x[m] * ew;
                    w += ew;
                }
            }

            if (useDown)
            {
                foreach (var (m, ew) in _down[n])
                {
                    sum += _x[m] * ew;
                    w += ew;
                }
            }

            (target[i], weight[i]) = w > 0 ? (sum / w - offset[i], w) : (_x[n] - offset[i], 0.01);
        }

        var solved = Isotonic(target, weight);
        for (var i = 0; i < k; i++)
        {
            _x[layer[i]] = solved[i] + offset[i];
        }
    }

    private static double[] Isotonic(double[] values, double[] weights)
    {
        var blockValue = new List<double>();
        var blockWeight = new List<double>();
        var blockCount = new List<int>();
        for (var i = 0; i < values.Length; i++)
        {
            blockValue.Add(values[i]);
            blockWeight.Add(weights[i]);
            blockCount.Add(1);
            while (blockValue.Count > 1 && blockValue[^2] > blockValue[^1])
            {
                var w = blockWeight[^2] + blockWeight[^1];
                var v = (blockValue[^2] * blockWeight[^2] + blockValue[^1] * blockWeight[^1]) / w;
                var c = blockCount[^2] + blockCount[^1];
                blockValue.RemoveAt(blockValue.Count - 1);
                blockWeight.RemoveAt(blockWeight.Count - 1);
                blockCount.RemoveAt(blockCount.Count - 1);
                blockValue[^1] = v;
                blockWeight[^1] = w;
                blockCount[^1] = c;
            }
        }

        var result = new double[values.Length];
        var index = 0;
        for (var b = 0; b < blockValue.Count; b++)
        {
            for (var j = 0; j < blockCount[b]; j++)
            {
                result[index++] = blockValue[b];
            }
        }

        return result;
    }

    /// <summary>Keeps nodes that are not members of a cluster out of the cluster's horizontal extent.</summary>
    private void PushOutOfClusters()
    {
        if (_clusters.Length == 0)
        {
            return;
        }

        var order = Enumerable.Range(0, _clusters.Length).OrderByDescending(c => _clusterDepth[c]).ToList();
        for (var round = 0; round < 2; round++)
        {
            foreach (var c in order)
            {
                var members = Enumerable.Range(0, _w.Count).Where(n => _nodePath[n].Contains(c)).ToList();
                if (members.Count == 0)
                {
                    continue;
                }

                var pad = _clusters[c].Padding;
                var min = members.Min(n => _x[n] - Width(n) / 2) - pad - LabelSide(c);
                var max = members.Max(n => _x[n] + Width(n) / 2) + pad;
                if (!_horizontal && max - min < _clusters[c].LabelWidth + 2 * pad)
                {
                    var grow = (_clusters[c].LabelWidth + 2 * pad - (max - min)) / 2;
                    min -= grow;
                    max += grow;
                }

                var minRank = members.Min(n => _rank[n]);
                var maxRank = members.Max(n => _rank[n]);
                for (var r = minRank; r <= maxRank; r++)
                {
                    var layer = _layers[r];
                    var first = layer.FindIndex(n => _nodePath[n].Contains(c));
                    var last = layer.FindLastIndex(n => _nodePath[n].Contains(c));
                    if (first < 0)
                    {
                        var mid = (min + max) / 2;
                        first = layer.FindIndex(n => _x[n] >= mid);
                        if (first < 0)
                        {
                            first = layer.Count;
                        }

                        last = first - 1;
                    }

                    if (first > 0)
                    {
                        var j = first - 1;
                        var limit = min - (_dummy[layer[j]] ? _edgeSep : _nodeSep / 2) - Width(layer[j]) / 2;
                        for (; j >= 0; j--)
                        {
                            if (_x[layer[j]] <= limit)
                            {
                                break;
                            }

                            _x[layer[j]] = limit;
                            if (j > 0)
                            {
                                limit = _x[layer[j]] - Separation(layer[j - 1], layer[j]);
                            }
                        }
                    }

                    if (last < layer.Count - 1)
                    {
                        var j = last + 1;
                        var limit = max + (_dummy[layer[j]] ? _edgeSep : _nodeSep / 2) + Width(layer[j]) / 2;
                        for (; j < layer.Count; j++)
                        {
                            if (_x[layer[j]] >= limit)
                            {
                                break;
                            }

                            _x[layer[j]] = limit;
                            if (j + 1 < layer.Count)
                            {
                                limit = _x[layer[j]] + Separation(layer[j], layer[j + 1]);
                            }
                        }
                    }
                }
            }
        }
    }

    private void AssignY()
    {
        var ranks = _layers.Length;
        var height = new double[ranks];
        var top = new double[ranks];
        var bottom = new double[ranks];
        var minRank = new int[_clusters.Length];
        var maxRank = new int[_clusters.Length];
        Array.Fill(minRank, int.MaxValue);
        Array.Fill(maxRank, int.MinValue);
        for (var n = 0; n < _w.Count; n++)
        {
            foreach (var c in _nodePath[n])
            {
                minRank[c] = Math.Min(minRank[c], _rank[n]);
                maxRank[c] = Math.Max(maxRank[c], _rank[n]);
            }
        }

        var labelAtBottom = _direction == LayoutDirection.BottomToTop;
        for (var r = 0; r < ranks; r++)
        {
            foreach (var n in _layers[r])
            {
                height[r] = Math.Max(height[r], _h[n]);
                double t = 0, b = 0;
                foreach (var c in _nodePath[n])
                {
                    var cluster = _clusters[c];
                    var label = !_horizontal && cluster.LabelHeight > 0 ? cluster.LabelHeight + cluster.Padding / 2 : 0;
                    if (minRank[c] == r)
                    {
                        t += cluster.Padding + (labelAtBottom ? 0 : label);
                    }

                    if (maxRank[c] == r)
                    {
                        b += cluster.Padding + (labelAtBottom ? label : 0);
                    }
                }

                top[r] = Math.Max(top[r], t);
                bottom[r] = Math.Max(bottom[r], b);
            }
        }

        var gap = _rankSep / 2;
        var y = 0.0;
        for (var r = 0; r < ranks; r++)
        {
            y += (r == 0 ? 0 : gap) + top[r] + height[r] / 2;
            foreach (var n in _layers[r])
            {
                _y[n] = y;
            }

            y += height[r] / 2 + bottom[r];
        }
    }

    private (Rect[] Bounds, Rect[] Labels) ClusterRects()
    {
        var bounds = new Rect[_clusters.Length];
        var labels = new Rect[_clusters.Length];
        var done = new bool[_clusters.Length];
        foreach (var c in Enumerable.Range(0, _clusters.Length).OrderByDescending(c => _clusterDepth[c]))
        {
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            for (var n = 0; n < _w.Count; n++)
            {
                if (_owner[n] == c)
                {
                    minX = Math.Min(minX, _x[n] - _w[n] / 2);
                    maxX = Math.Max(maxX, _x[n] + _w[n] / 2);
                    minY = Math.Min(minY, _y[n] - _h[n] / 2);
                    maxY = Math.Max(maxY, _y[n] + _h[n] / 2);
                }
            }

            for (var child = 0; child < _clusters.Length; child++)
            {
                if (_clusters[child].Parent == c && done[child])
                {
                    var b = bounds[child];
                    minX = Math.Min(minX, b.X);
                    maxX = Math.Max(maxX, b.Right);
                    minY = Math.Min(minY, b.Y);
                    maxY = Math.Max(maxY, b.Bottom);
                }
            }

            if (double.IsInfinity(minX))
            {
                continue;
            }

            var cluster = _clusters[c];
            var pad = cluster.Padding;
            minX -= pad;
            maxX += pad;
            minY -= pad;
            maxY += pad;
            var (lw, lh) = (cluster.LabelWidth, cluster.LabelHeight);
            Rect label;
            if (_horizontal)
            {
                // Internal x maps to the final y axis: the label sits on the internal left side.
                if (lh > 0)
                {
                    minX -= lh + pad / 2;
                }

                if (maxY - minY < lw + 2 * pad)
                {
                    var grow = (lw + 2 * pad - (maxY - minY)) / 2;
                    minY -= grow;
                    maxY += grow;
                }

                label = new Rect(minX + pad / 2, (minY + maxY) / 2 - lw / 2, lh, lw);
            }
            else
            {
                if (maxX - minX < lw + 2 * pad)
                {
                    var grow = (lw + 2 * pad - (maxX - minX)) / 2;
                    minX -= grow;
                    maxX += grow;
                }

                if (lh > 0)
                {
                    if (_direction == LayoutDirection.BottomToTop)
                    {
                        maxY += lh + pad / 2;
                    }
                    else
                    {
                        minY -= lh + pad / 2;
                    }
                }

                var labelY = _direction == LayoutDirection.BottomToTop ? maxY - pad / 2 - lh : minY + pad / 2;
                label = new Rect((minX + maxX) / 2 - lw / 2, labelY, lw, lh);
            }

            bounds[c] = new Rect(minX, minY, maxX - minX, maxY - minY);
            labels[c] = label;
            done[c] = true;
        }

        return (bounds, labels);
    }
}
