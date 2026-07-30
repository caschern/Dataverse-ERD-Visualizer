using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using DataverseErdVisualizer.Models;
using DataverseErdVisualizer.Rendering;

namespace DataverseErdVisualizer.Layout
{
    /// <summary>
    /// Layered (Sugiyama-style) layout adapted for ERDs: 1:N edges rank the
    /// "one" (referenced) table above the "many" (referencing) table, cycles
    /// are detected and routed as right-hand rail loops, self-referential
    /// edges are excluded (drawn as loops on the box), and edges between the
    /// same pair of tables fan out with per-edge anchor offsets.
    /// Ported from the Dataverse Process Mapper engine minus its
    /// container-cohesion logic (ERDs have no nested scopes).
    /// </summary>
    public static class ErdLayoutEngine
    {
        /// <summary>Lays out the graph and returns the overall canvas size.</summary>
        public static SizeF Layout(ErdGraph graph)
        {
            if (graph.Nodes.Count == 0) return new SizeF(300, 120);

            foreach (var e in graph.Edges)
            {
                e.IsBack = false;
                e.LaneY = null;
                e.RailX = null;
                e.Route = null;
                e.FromPortX = null;
                e.ToPortX = null;
            }

            // Self-loops never take part in ranking or routing.
            var routable = graph.Edges.Where(e => !e.IsSelf).ToList();

            MarkBackEdges(graph, routable);

            var forward = routable.Where(e => !e.IsBack).ToList();
            AssignRanks(graph, forward);

            // Multi-rank edges are split into per-gap segments joined by virtual
            // waypoint nodes that reserve a clear channel through every rank.
            var virtuals = new List<ErdNode>();
            var chains = new List<Chain>();
            var layoutForward = BuildLayoutEdges(graph, forward, virtuals, chains);
            var allNodes = graph.Nodes.Concat(virtuals).ToList();
            var byId = new Dictionary<string, ErdNode>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in allNodes) byId[n.Id] = n;

            var ranks = OrderRanks(allNodes, layoutForward);
            return Position(graph, ranks, layoutForward, byId, chains);
        }

        /// <summary>
        /// Horizontal offset of this edge's anchor point on the given node, so
        /// parallel relationships between the same tables don't overlap.
        /// </summary>
        public static float AnchorOffset(ErdEdge edge, ErdNode node)
        {
            if (edge.ParallelCount <= 1 || node == null) return 0f;
            float off = (edge.ParallelIndex - (edge.ParallelCount - 1) / 2f) * ErdStyle.ParallelSpacing;
            float max = node.Bounds.Width / 2f - 14f;
            if (max < 0f) max = 0f;
            if (off > max) off = max;
            if (off < -max) off = -max;
            return off;
        }

        private const float VirtualWidth = 10f;

        private class Chain
        {
            public ErdEdge Original;
            public List<ErdNode> Vias = new List<ErdNode>();
            public List<ErdEdge> Segments = new List<ErdEdge>();
        }

        /// <summary>
        /// Splits multi-rank forward edges into chains of adjacent-rank segments
        /// joined by virtual nodes; adjacent edges pass through unchanged.
        /// </summary>
        private static List<ErdEdge> BuildLayoutEdges(ErdGraph graph, List<ErdEdge> forward,
            List<ErdNode> virtuals, List<Chain> chains)
        {
            var layout = new List<ErdEdge>();
            int counter = 0;
            foreach (var e in forward)
            {
                var from = graph[e.FromId];
                var to = graph[e.ToId];
                if (from == null || to == null) continue;
                if (to.Rank - from.Rank <= 1)
                {
                    layout.Add(e);
                    continue;
                }

                var chain = new Chain { Original = e };
                string prev = e.FromId;
                for (int r = from.Rank + 1; r < to.Rank; r++)
                {
                    var via = new ErdNode
                    {
                        Id = "__v" + counter++,
                        Title = "",
                        Rank = r,
                        Bounds = new RectangleF(0f, 0f, VirtualWidth, 1f)
                    };
                    virtuals.Add(via);
                    chain.Vias.Add(via);
                    var seg = new ErdEdge { FromId = prev, ToId = via.Id };
                    chain.Segments.Add(seg);
                    layout.Add(seg);
                    prev = via.Id;
                }
                var lastSeg = new ErdEdge { FromId = prev, ToId = e.ToId };
                chain.Segments.Add(lastSeg);
                layout.Add(lastSeg);
                chains.Add(chain);
            }
            return layout;
        }

        /// <summary>
        /// Crossing reduction: orders the nodes of each rank by the barycenter
        /// (average position) of their neighbors in the adjacent rank, sweeping
        /// down and up a few times.
        /// </summary>
        private static List<List<ErdNode>> OrderRanks(List<ErdNode> allNodes, List<ErdEdge> forward)
        {
            var ranks = allNodes
                .GroupBy(n => n.Rank)
                .OrderBy(g => g.Key)
                .Select(g => g.ToList())
                .ToList();

            var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var rank in ranks)
                for (int i = 0; i < rank.Count; i++)
                    order[rank[i].Id] = i;

            var parents = forward.GroupBy(e => e.ToId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(e => e.FromId).ToList(), StringComparer.OrdinalIgnoreCase);
            var children = forward.GroupBy(e => e.FromId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ToId).ToList(), StringComparer.OrdinalIgnoreCase);

            for (int iter = 0; iter < 4; iter++)
            {
                for (int r = 1; r < ranks.Count; r++)          // downward: follow parents
                    SortByBarycenter(ranks[r], parents, order);
                for (int r = ranks.Count - 2; r >= 0; r--)     // upward: follow children
                    SortByBarycenter(ranks[r], children, order);
            }

            return ranks;
        }

        private static void SortByBarycenter(List<ErdNode> rank,
            Dictionary<string, List<string>> neighbors, Dictionary<string, int> order)
        {
            var barycenter = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rank.Count; i++)
            {
                var node = rank[i];
                float value = i; // nodes without neighbors keep their position
                if (neighbors.TryGetValue(node.Id, out var ids) && ids.Count > 0)
                {
                    float sum = 0;
                    int count = 0;
                    foreach (var id in ids)
                        if (order.TryGetValue(id, out var o)) { sum += o; count++; }
                    if (count > 0) value = sum / count;
                }
                barycenter[node.Id] = value;
            }

            var sorted = rank.OrderBy(n => barycenter[n.Id]).ToList(); // stable
            rank.Clear();
            rank.AddRange(sorted);
            for (int i = 0; i < rank.Count; i++)
                order[rank[i].Id] = i;
        }

        /// <summary>DFS to flag edges that return to an ancestor (cycles).</summary>
        private static void MarkBackEdges(ErdGraph graph, List<ErdEdge> routable)
        {
            var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // 0=unseen,1=in-stack,2=done
            foreach (var n in graph.Nodes) state[n.Id] = 0;

            var edgeLookup = routable
                .GroupBy(e => e.FromId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(grp => grp.Key, grp => grp.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var root in Roots(graph, routable))
                Visit(root, state, edgeLookup);

            // Any node not reached (disconnected component) — visit too.
            foreach (var n in graph.Nodes)
                if (state[n.Id] == 0)
                    Visit(n.Id, state, edgeLookup);
        }

        private static void Visit(string id, Dictionary<string, int> state, Dictionary<string, List<ErdEdge>> edges)
        {
            state[id] = 1;
            if (edges.TryGetValue(id, out var outs))
            {
                foreach (var e in outs)
                {
                    if (!state.ContainsKey(e.ToId)) continue;
                    if (state[e.ToId] == 1) e.IsBack = true;       // edge to an in-stack ancestor
                    else if (state[e.ToId] == 0) Visit(e.ToId, state, edges);
                }
            }
            state[id] = 2;
        }

        private static List<string> Roots(ErdGraph graph, List<ErdEdge> routable)
        {
            var hasIncoming = new HashSet<string>(routable.Select(e => e.ToId), StringComparer.OrdinalIgnoreCase);
            var roots = graph.Nodes.Where(n => !hasIncoming.Contains(n.Id)).Select(n => n.Id).ToList();
            if (roots.Count == 0 && graph.Nodes.Count > 0)
                roots.Add(graph.Nodes[0].Id);
            return roots;
        }

        /// <summary>Longest-path ranking over the acyclic (forward) edge set.</summary>
        private static void AssignRanks(ErdGraph graph, List<ErdEdge> forward)
        {
            foreach (var n in graph.Nodes) n.Rank = 0;

            var incoming = forward.GroupBy(e => e.ToId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(e => e.FromId).ToList(), StringComparer.OrdinalIgnoreCase);

            // Iterate to a fixed point (graph is a DAG on forward edges).
            bool changed = true;
            int guard = 0;
            while (changed && guard++ < graph.Nodes.Count + 2)
            {
                changed = false;
                foreach (var n in graph.Nodes)
                {
                    if (!incoming.TryGetValue(n.Id, out var preds)) continue;
                    int best = 0;
                    foreach (var p in preds)
                    {
                        var pn = graph[p];
                        if (pn != null && pn.Rank + 1 > best) best = pn.Rank + 1;
                    }
                    if (best != n.Rank) { n.Rank = best; changed = true; }
                }
            }
        }

        // Lane geometry: distance between parallel horizontal runs, and the
        // minimum horizontal clearance for two runs to share a lane.
        private const float LaneSpacing = 10f;
        private const float LaneMinSeparation = 12f;

        private static SizeF Position(ErdGraph graph, List<List<ErdNode>> ranks,
            List<ErdEdge> layoutForward, Dictionary<string, ErdNode> byId, List<Chain> chains)
        {
            // Per-rank height = tallest box in that rank (boxes are top-aligned).
            var rankHeights = ranks.Select(r => r.Max(n => n.Bounds.Height)).ToList();

            // --- X pass: feasible packed start, then median alignment ---
            foreach (var rank in ranks)
            {
                float x = ErdStyle.Margin;
                foreach (var node in rank)
                {
                    node.Bounds = new RectangleF(x, 0f, node.Bounds.Width, node.Bounds.Height);
                    x += node.Bounds.Width + ErdStyle.HorizontalGap;
                }
            }
            AlignColumns(ranks, layoutForward, byId);

            // Normalize: leftmost node at the margin, canvas hugs the content.
            float minX = float.MaxValue, maxRight = float.MinValue;
            foreach (var rank in ranks)
                foreach (var node in rank)
                {
                    if (node.Bounds.X < minX) minX = node.Bounds.X;
                    if (node.Bounds.Right > maxRight) maxRight = node.Bounds.Right;
                }
            float shiftAll = ErdStyle.Margin - minX;
            foreach (var rank in ranks)
                foreach (var node in rank)
                {
                    var b = node.Bounds;
                    b.X += shiftAll;
                    node.Bounds = b;
                }
            float canvasWidth = maxRight + shiftAll + ErdStyle.Margin;

            // Every edge gets its own entry/exit port on its boxes, so lines,
            // crow's feet and rotated labels never converge on one point.
            AssignPorts(graph, chains);

            // --- Routing pass: give every horizontal run its own lane ---
            var rankIndexByValue = new Dictionary<int, int>();
            for (int r = 0; r < ranks.Count; r++)
                rankIndexByValue[ranks[r][0].Rank] = r;

            var laneOfEdge = new Dictionary<ErdEdge, KeyValuePair<int, int>>(); // edge -> (gap, lane)
            var laneCount = new int[Math.Max(1, ranks.Count)];
            RouteForwardEdges(graph, byId, rankIndexByValue, laneOfEdge, laneCount, layoutForward);

            // --- Y pass: rows separated by gaps stretched to fit their lanes ---
            var rowTops = new float[ranks.Count];
            var gapHeights = new float[ranks.Count]; // gap BELOW rank r
            float y = ErdStyle.Margin + ErdStyle.TitleBandHeight;
            for (int r = 0; r < ranks.Count; r++)
            {
                rowTops[r] = y;
                float rowHeight = rankHeights[r];
                foreach (var node in ranks[r])
                    node.Bounds = new RectangleF(node.Bounds.X, y, node.Bounds.Width, node.Bounds.Height);

                float gap = 0f;
                if (r < ranks.Count - 1)
                    gap = Math.Max(ErdStyle.VerticalGap, laneCount[r] * LaneSpacing + 20f)
                          + graph.ExtraRankGap;
                gapHeights[r] = gap;
                y += rowHeight + gap;
            }
            float canvasHeight = y + ErdStyle.Margin;

            // --- Absolute lane Y per routed edge (lanes centered in their gap) ---
            foreach (var kv in laneOfEdge)
            {
                int gap = kv.Value.Key;
                int lane = kv.Value.Value;
                float gapTop = rowTops[gap] + rankHeights[gap];
                float firstLane = gapTop + (gapHeights[gap] - (laneCount[gap] - 1) * LaneSpacing) / 2f;
                kv.Key.LaneY = firstLane + lane * LaneSpacing;
            }

            // --- Assemble full polylines for the virtual-node chains ---
            BuildRoutes(graph, chains);

            // --- Back-edge rails: overlapping loops each get their own rail ---
            float maxRail = AssignBackEdgeRails(graph);
            if (maxRail > 0f)
                canvasWidth = Math.Max(canvasWidth, maxRail + ErdStyle.Margin);

            // Rail label chips hang to the right of their rail — keep them on canvas.
            foreach (var e in graph.Edges)
            {
                if (!e.IsBack || e.IsSelf || e.RailX == null || string.IsNullOrEmpty(e.Label)) continue;
                canvasWidth = Math.Max(canvasWidth, e.RailX.Value + e.Label.Length * 6.5f + 16f + ErdStyle.Margin);
            }

            // Self-loops stick out to the right of their box.
            foreach (var e in graph.Edges)
            {
                if (!e.IsSelf) continue;
                var n = graph[e.FromId];
                if (n != null)
                    canvasWidth = Math.Max(canvasWidth, n.Bounds.Right + 60f + ErdStyle.Margin);
            }

            return new SizeF(canvasWidth, canvasHeight);
        }

        /// <summary>
        /// Spreads each box's incident edges across its bottom (outgoing) and
        /// top (incoming) borders, ordered by where the other endpoint sits so
        /// connectors fan out instead of crossing. Distinct ports are what keep
        /// crow's feet and the rotated relationship labels from piling onto the
        /// box's center line.
        /// </summary>
        private static void AssignPorts(ErdGraph graph, List<Chain> chains)
        {
            var routable = graph.Edges.Where(e => !e.IsSelf && !e.IsBack).ToList();

            float OtherCenterX(string id)
            {
                var other = graph[id];
                return other == null ? 0f : other.Bounds.X + other.Bounds.Width / 2f;
            }

            foreach (var group in routable.GroupBy(e => e.FromId, StringComparer.OrdinalIgnoreCase))
            {
                var node = graph[group.Key];
                if (node == null) continue;
                var ordered = group
                    .OrderBy(e => OtherCenterX(e.ToId))
                    .ThenBy(e => e.ParallelIndex)
                    .ThenBy(e => e.Relationship?.SchemaName ?? "", StringComparer.OrdinalIgnoreCase)
                    .ToList();
                SpreadPorts(node, ordered, graph.PortSpacing, (e, x) => e.FromPortX = x);
            }

            foreach (var group in routable.GroupBy(e => e.ToId, StringComparer.OrdinalIgnoreCase))
            {
                var node = graph[group.Key];
                if (node == null) continue;
                var ordered = group
                    .OrderBy(e => OtherCenterX(e.FromId))
                    .ThenBy(e => e.ParallelIndex)
                    .ThenBy(e => e.Relationship?.SchemaName ?? "", StringComparer.OrdinalIgnoreCase)
                    .ToList();
                SpreadPorts(node, ordered, graph.PortSpacing, (e, x) => e.ToPortX = x);
            }

            // Multi-rank chains route as segments: the real ports live on the
            // first and last segment; the vias in between keep their own X.
            foreach (var chain in chains)
            {
                if (chain.Segments.Count == 0) continue;
                chain.Segments[0].FromPortX = chain.Original.FromPortX;
                chain.Segments[chain.Segments.Count - 1].ToPortX = chain.Original.ToPortX;
            }
        }

        private static void SpreadPorts(ErdNode node, List<ErdEdge> edges, float portSpacing,
            Action<ErdEdge, float> set)
        {
            int n = edges.Count;
            float cx = node.Bounds.X + node.Bounds.Width / 2f;
            if (n == 1)
            {
                set(edges[0], cx);
                return;
            }

            float usable = Math.Max(0f, node.Bounds.Width - 28f);
            float spacing = Math.Min(portSpacing, usable / (n - 1));
            for (int i = 0; i < n; i++)
                set(edges[i], cx + (i - (n - 1) / 2f) * spacing);
        }

        /// <summary>
        /// Turns each chain's segment lanes and via positions into the original
        /// edge's full polyline. Straight stretches collapse away; jogs happen
        /// on the segments' assigned lanes.
        /// </summary>
        private static void BuildRoutes(ErdGraph graph, List<Chain> chains)
        {
            foreach (var chain in chains)
            {
                var from = graph[chain.Original.FromId];
                var to = graph[chain.Original.ToId];
                if (from == null || to == null) continue;

                float fromX = chain.Original.FromPortX ?? (CenterX(from) + AnchorOffset(chain.Original, from));
                float toX = chain.Original.ToPortX ?? (CenterX(to) + AnchorOffset(chain.Original, to));

                var stops = new List<ErdNode> { from };
                stops.AddRange(chain.Vias);
                stops.Add(to);

                var xs = new List<float> { fromX };
                for (int i = 0; i < chain.Vias.Count; i++) xs.Add(CenterX(chain.Vias[i]));
                xs.Add(toX);

                var pts = new List<PointF> { new PointF(fromX, from.Bounds.Bottom) };
                for (int i = 0; i < chain.Segments.Count; i++)
                {
                    float ax = xs[i];
                    float bx = xs[i + 1];
                    if (Math.Abs(ax - bx) < 0.5f) continue; // straight through this gap
                    float laneY = chain.Segments[i].LaneY
                        ?? (stops[i].Bounds.Bottom + stops[i + 1].Bounds.Y) / 2f;
                    pts.Add(new PointF(ax, laneY));
                    pts.Add(new PointF(bx, laneY));
                }
                pts.Add(new PointF(toX, to.Bounds.Y));
                chain.Original.Route = pts;
            }
        }

        // ---------- x-coordinate alignment ----------

        private const int AlignmentSweeps = 3;

        /// <summary>
        /// Pulls every node toward the median X of its parents (down sweeps) or
        /// children (up sweeps) while preserving the crossing-reduced order and
        /// minimum spacing, so most edges become straight vertical drops.
        /// </summary>
        private static void AlignColumns(List<List<ErdNode>> ranks,
            List<ErdEdge> layoutForward, Dictionary<string, ErdNode> byId)
        {
            var parents = BuildNeighborMap(layoutForward, byId, incoming: true);
            var children = BuildNeighborMap(layoutForward, byId, incoming: false);

            for (int sweep = 0; sweep < AlignmentSweeps; sweep++)
            {
                if (sweep % 2 == 0)
                {
                    for (int r = 1; r < ranks.Count; r++)
                        PlaceRank(ranks[r], parents);
                }
                else
                {
                    for (int r = ranks.Count - 2; r >= 0; r--)
                        PlaceRank(ranks[r], children);
                }
            }
        }

        private static Dictionary<string, List<ErdNode>> BuildNeighborMap(
            List<ErdEdge> forward, Dictionary<string, ErdNode> byId, bool incoming)
        {
            var map = new Dictionary<string, List<ErdNode>>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in forward)
            {
                var key = incoming ? e.ToId : e.FromId;
                if (!byId.TryGetValue(incoming ? e.FromId : e.ToId, out var other)) continue;
                if (!byId.ContainsKey(key)) continue;
                if (!map.TryGetValue(key, out var list))
                    map[key] = list = new List<ErdNode>();
                list.Add(other);
            }
            return map;
        }

        private static float CenterX(ErdNode n) => n.Bounds.X + n.Bounds.Width / 2f;

        /// <summary>
        /// Places one rank at the nodes' desired centers using cluster merging:
        /// when neighbors want overlapping spots they fuse into a cluster placed
        /// at the mean of their desires, keeping order and minimum separation.
        /// </summary>
        private static void PlaceRank(List<ErdNode> rank, Dictionary<string, List<ErdNode>> neighbors)
        {
            int n = rank.Count;

            var desired = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (neighbors.TryGetValue(rank[i].Id, out var ns) && ns.Count > 0)
                {
                    var xs = ns.Select(CenterX).OrderBy(x => x).ToList();
                    desired[i] = xs.Count % 2 == 1
                        ? xs[xs.Count / 2]
                        : (xs[xs.Count / 2 - 1] + xs[xs.Count / 2]) / 2f;
                }
                else
                {
                    desired[i] = CenterX(rank[i]); // nothing to align to: stay put
                }
            }

            // Minimum center-to-center distance between node i-1 and node i.
            var sep = new float[n];
            for (int i = 1; i < n; i++)
                sep[i] = (rank[i - 1].Bounds.Width + rank[i].Bounds.Width) / 2f + ErdStyle.HorizontalGap;

            var clusters = new List<Cluster>();
            for (int i = 0; i < n; i++)
            {
                clusters.Add(new Cluster
                {
                    First = i,
                    Last = i,
                    Count = 1,
                    Sum = desired[i],
                    OffsetLast = 0f
                });
                clusters[clusters.Count - 1].Position = desired[i];

                // Merge with the previous cluster while they would overlap.
                while (clusters.Count >= 2)
                {
                    var b = clusters[clusters.Count - 1];
                    var a = clusters[clusters.Count - 2];
                    float gap = sep[b.First]; // b.First >= 1 whenever a exists
                    if (a.Position + a.OffsetLast + gap <= b.Position) break;

                    float shift = a.OffsetLast + gap; // b's first member offset in merged cluster
                    a.Sum += b.Sum - b.Count * shift;
                    a.Count += b.Count;
                    a.OffsetLast = shift + b.OffsetLast;
                    a.Last = b.Last;
                    a.Position = a.Sum / a.Count;
                    clusters.RemoveAt(clusters.Count - 1);
                }
            }

            foreach (var cluster in clusters)
            {
                float offset = 0f;
                for (int i = cluster.First; i <= cluster.Last; i++)
                {
                    if (i > cluster.First) offset += sep[i];
                    float center = cluster.Position + offset;
                    var b = rank[i].Bounds;
                    b.X = center - b.Width / 2f;
                    rank[i].Bounds = b;
                }
            }
        }

        private class Cluster
        {
            public int First;
            public int Last;
            public int Count;
            public float Sum;        // sum of (desired center - offset within cluster)
            public float OffsetLast; // offset of the last member from the first
            public float Position;   // center X of the first member
        }

        /// <summary>
        /// Interval-graph coloring per gap: two runs may share a lane when they
        /// don't overlap horizontally, or when they share a source (fan-out) or
        /// target (fan-in) — those merge into a single visual "bus".
        /// </summary>
        private static void RouteForwardEdges(ErdGraph graph, Dictionary<string, ErdNode> byId,
            Dictionary<int, int> rankIndex,
            Dictionary<ErdEdge, KeyValuePair<int, int>> laneOfEdge, int[] laneCount,
            List<ErdEdge> layoutForward)
        {
            var byGap = new Dictionary<int, List<Run>>();
            foreach (var e in layoutForward)
            {
                if (!byId.TryGetValue(e.FromId, out var from)) continue;
                if (!byId.TryGetValue(e.ToId, out var to)) continue;
                if (!rankIndex.TryGetValue(to.Rank, out var targetRank)) continue;
                int gap = targetRank - 1;
                if (gap < 0 || gap >= laneCount.Length) continue;

                float sx = e.FromPortX ?? (CenterX(from) + AnchorOffset(e, from));
                float tx = e.ToPortX ?? (CenterX(to) + AnchorOffset(e, to));
                bool straight = Math.Abs(sx - tx) < 0.5f;

                // Straight drops need no routing lane (their labels ride the
                // drop itself as rotated port labels).
                if (straight) continue;

                if (!byGap.TryGetValue(gap, out var list))
                    byGap[gap] = list = new List<Run>();
                list.Add(new Run
                {
                    Edge = e,
                    Left = Math.Min(sx, tx),
                    Right = Math.Max(sx, tx)
                });
            }

            foreach (var kv in byGap)
            {
                var runs = kv.Value.OrderBy(s => s.Left).ThenBy(s => s.Right).ToList();
                var lanes = new List<List<Run>>();
                foreach (var run in runs)
                {
                    int laneIdx = -1;
                    for (int i = 0; i < lanes.Count && laneIdx < 0; i++)
                    {
                        bool fits = true;
                        foreach (var other in lanes[i])
                        {
                            bool overlaps = run.Left < other.Right + LaneMinSeparation &&
                                            other.Left < run.Right + LaneMinSeparation;
                            // Fan-in/fan-out edges may share a lane (they merge
                            // into one bus).
                            bool sharesFlow = run.Edge.FromId == other.Edge.FromId ||
                                              run.Edge.ToId == other.Edge.ToId;
                            if (overlaps && !sharesFlow) { fits = false; break; }
                        }
                        if (fits) laneIdx = i;
                    }
                    if (laneIdx < 0)
                    {
                        laneIdx = lanes.Count;
                        lanes.Add(new List<Run>());
                    }
                    lanes[laneIdx].Add(run);
                    laneOfEdge[run.Edge] = new KeyValuePair<int, int>(kv.Key, laneIdx);
                }
                laneCount[kv.Key] = lanes.Count;
            }
        }

        private class Run
        {
            public ErdEdge Edge;
            public float Left;
            public float Right;
        }

        /// <summary>Assigns each back edge a right-hand rail; vertically overlapping loops get distinct rails.</summary>
        private static float AssignBackEdgeRails(ErdGraph graph)
        {
            var backs = new List<Rail>();
            foreach (var e in graph.Edges)
            {
                if (!e.IsBack || e.IsSelf) continue;
                var from = graph[e.FromId];
                var to = graph[e.ToId];
                if (from == null || to == null) continue;
                float y1 = from.Bounds.Y + from.Bounds.Height / 2f;
                float y2 = to.Bounds.Y + to.Bounds.Height / 2f;
                float top = Math.Min(y1, y2);
                float bottom = Math.Max(y1, y2);

                // The rail must clear EVERY box whose vertical span it passes,
                // not just the two endpoints — ERD ranks are wide.
                float baseX = Math.Max(from.Bounds.Right, to.Bounds.Right);
                foreach (var n in graph.Nodes)
                {
                    if (n.Bounds.Bottom < top || n.Bounds.Y > bottom) continue;
                    if (n.Bounds.Right > baseX) baseX = n.Bounds.Right;
                }

                backs.Add(new Rail
                {
                    Edge = e,
                    Top = top,
                    Bottom = bottom,
                    BaseX = baseX
                });
            }
            if (backs.Count == 0) return 0f;

            float maxRail = 0f;
            var laneBottom = new List<float>();
            foreach (var b in backs.OrderBy(b => b.Top))
            {
                int lane = -1;
                for (int i = 0; i < laneBottom.Count; i++)
                {
                    if (laneBottom[i] + 8f <= b.Top) { lane = i; break; }
                }
                if (lane < 0) { lane = laneBottom.Count; laneBottom.Add(b.Bottom); }
                else laneBottom[lane] = b.Bottom;

                float rail = b.BaseX + 28f + lane * 14f;
                b.Edge.RailX = rail;
                if (rail > maxRail) maxRail = rail;
            }
            return maxRail;
        }

        private class Rail
        {
            public ErdEdge Edge;
            public float Top;
            public float Bottom;
            public float BaseX;
        }
    }
}
