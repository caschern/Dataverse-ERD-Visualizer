using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using DataverseErdVisualizer.Models;

namespace DataverseErdVisualizer.Layout
{
    /// <summary>
    /// Packs "satellite" tables — those whose relationships all point at a
    /// single hub — into a compact grid beside that hub, instead of letting a
    /// hub's satellites stretch one rank across tens of thousands of pixels.
    ///
    /// A satellite's horizontal position carries no information: every one of
    /// its connections goes to the same table, so it can sit anywhere without
    /// hiding structure. Note the test is "one distinct NEIGHBOUR", not "one
    /// relationship" — carrying several lookups to the same hub (Assigned
    /// Judge + Assigned DCA -> Party) is extremely common and must still count
    /// as a satellite. Two kinds of cluster form:
    ///   • children  — tables carrying lookups TO the hub → grid below it
    ///   • parents   — the hub's own lookup/reference tables → grid above it
    ///
    /// The pack runs as a pre-layout transform: satellites are replaced by one
    /// placeholder box per cluster, the normal layered layout runs untouched on
    /// that reduced graph, then each placeholder is expanded into a grid and
    /// the satellite connectors are routed as an orthogonal bus (a trunk from
    /// the hub, a spine beside the grid, one rail per row, and a short stub
    /// into each box where the relationship label rides).
    /// </summary>
    public static class LeafClusterLayout
    {
        /// <summary>Below this, a normal rank reads better than a grid.</summary>
        public const int MinClusterSize = 5;

        private const float ColumnGap = 24f;
        private const float RowGap = 84f;      // must exceed RailInset
        private const float RailInset = 60f;   // rail offset from the row's near edge
        private const float SpineGutter = 22f; // reserved lane for the bus spine
        private const float TrunkOffset = 24f; // trunk offset from the hub border
        private const float TargetAspect = 1.7f;
        private const int MaxColumns = 16;
        private const float StubPitch = 18f;   // spacing of parallel stubs on one box

        public static SizeF Layout(ErdGraph graph, bool clusterSatellites,
            bool showAllSatelliteEdges = false)
        {
            foreach (var e in graph.Edges)
            {
                e.Hidden = false;
                e.CollapsedCount = 0;
            }

            if (!clusterSatellites) return ErdLayoutEngine.Layout(graph);

            var clusters = FindClusters(graph);
            if (clusters.Count == 0) return ErdLayoutEngine.Layout(graph);

            var reduced = BuildReducedGraph(graph, clusters);
            var canvas = ErdLayoutEngine.Layout(reduced);
            Expand(graph, clusters, showAllSatelliteEdges);
            return canvas;
        }

        /// <summary>Number of tables packed into grids (diagnostics/tests).</summary>
        public static int CountClusteredTables(ErdGraph graph)
            => FindClusters(graph).Sum(c => c.Members.Count);

        private class Member
        {
            public ErdNode Node;
            public List<ErdEdge> Edges = new List<ErdEdge>();
        }

        private class Cluster
        {
            public string AnchorId;
            public bool Below;              // grid sits below the hub
            public List<Member> Members = new List<Member>();
            public ErdNode Placeholder;
            public int Columns = 1;
            public float CellWidth;
            public List<float> RowHeights = new List<float>();
            public SizeF Size;
        }

        // ------------------------------------------------------------ detect

        private static List<Cluster> FindClusters(ErdGraph graph)
        {
            var neighbours = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var incident = new Dictionary<string, List<ErdEdge>>(StringComparer.OrdinalIgnoreCase);
            var selfLooped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var n in graph.Nodes)
            {
                neighbours[n.Id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                incident[n.Id] = new List<ErdEdge>();
            }

            foreach (var e in graph.Edges)
            {
                if (e.IsSelf) { selfLooped.Add(e.FromId); continue; }
                if (!neighbours.ContainsKey(e.FromId) || !neighbours.ContainsKey(e.ToId)) continue;
                neighbours[e.FromId].Add(e.ToId);
                neighbours[e.ToId].Add(e.FromId);
                incident[e.FromId].Add(e);
                incident[e.ToId].Add(e);
            }

            var groups = new Dictionary<string, Cluster>();
            foreach (var node in graph.Nodes)
            {
                if (selfLooped.Contains(node.Id)) continue;
                if (neighbours[node.Id].Count != 1) continue;

                var anchorId = neighbours[node.Id].First();
                if (graph[anchorId] == null) continue;
                // A two-table component is not a hub; leave it alone.
                if (neighbours[anchorId].Count <= 1) continue;

                var edges = incident[node.Id];
                // Direction of the majority decides which side of the hub the
                // grid sits on; each connector is still drawn its own way round.
                int asChild = edges.Count(e =>
                    string.Equals(e.ToId, node.Id, StringComparison.OrdinalIgnoreCase));
                bool below = asChild * 2 >= edges.Count;

                var key = anchorId.ToLowerInvariant() + (below ? "|below" : "|above");
                Cluster cluster;
                if (!groups.TryGetValue(key, out cluster))
                    groups[key] = cluster = new Cluster { AnchorId = anchorId, Below = below };
                cluster.Members.Add(new Member { Node = node, Edges = edges });
            }

            var result = groups.Values.Where(c => c.Members.Count >= MinClusterSize).ToList();
            foreach (var c in result) SortAndMeasure(c);
            return result;
        }

        /// <summary>
        /// Orders satellites alphabetically (a large grid is scanned to FIND a
        /// table, and relationship type is already visible on the edges), then
        /// picks the column count whose grid comes closest to the target aspect.
        /// </summary>
        private static void SortAndMeasure(Cluster c)
        {
            c.Members = c.Members
                .OrderBy(m => m.Node.Title ?? m.Node.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.Node.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int n = c.Members.Count;
            c.CellWidth = c.Members.Max(m => m.Node.Bounds.Width) + ColumnGap;
            float rowPitch = c.Members.Average(m => m.Node.Bounds.Height) + RowGap;

            float bestScore = float.MaxValue;
            for (int cols = 1; cols <= Math.Min(MaxColumns, n); cols++)
            {
                int rows = (int)Math.Ceiling(n / (double)cols);
                float score = Math.Abs((cols * c.CellWidth) / (rows * rowPitch) - TargetAspect);
                if (score < bestScore) { bestScore = score; c.Columns = cols; }
            }

            int rowCount = (int)Math.Ceiling(n / (double)c.Columns);
            c.RowHeights.Clear();
            for (int r = 0; r < rowCount; r++)
            {
                float max = 0f;
                for (int i = r * c.Columns; i < Math.Min(n, (r + 1) * c.Columns); i++)
                    max = Math.Max(max, c.Members[i].Node.Bounds.Height);
                c.RowHeights.Add(max);
            }

            float gridWidth = c.Columns * c.CellWidth - ColumnGap;
            float gridHeight = c.RowHeights.Sum() + (rowCount - 1) * RowGap;
            c.Size = new SizeF(SpineGutter + gridWidth, gridHeight);
        }

        // ------------------------------------------------------------ reduce

        private static ErdGraph BuildReducedGraph(ErdGraph graph, List<Cluster> clusters)
        {
            var memberIds = new HashSet<string>(
                clusters.SelectMany(c => c.Members).Select(m => m.Node.Id), StringComparer.OrdinalIgnoreCase);

            var reduced = new ErdGraph
            {
                Title = graph.Title,
                Subtitle = graph.Subtitle,
                ExtraRankGap = graph.ExtraRankGap,
                PortSpacing = graph.PortSpacing
            };

            // Node and edge instances are shared, so the layout writes its
            // geometry straight onto the real diagram objects.
            foreach (var n in graph.Nodes)
                if (!memberIds.Contains(n.Id))
                    reduced.AddNode(n);

            int index = 0;
            foreach (var c in clusters)
            {
                c.Placeholder = new ErdNode
                {
                    Id = "__cluster" + index++,
                    Title = "",
                    Bounds = new RectangleF(0f, 0f, c.Size.Width, c.Size.Height)
                };
                reduced.AddNode(c.Placeholder);
                reduced.AddEdge(new ErdEdge
                {
                    FromId = c.Below ? c.AnchorId : c.Placeholder.Id,
                    ToId = c.Below ? c.Placeholder.Id : c.AnchorId
                });
            }

            foreach (var e in graph.Edges)
            {
                if (memberIds.Contains(e.FromId) || memberIds.Contains(e.ToId)) continue;
                reduced.AddEdge(e);
            }

            return reduced;
        }

        // ------------------------------------------------------------ expand

        private static void Expand(ErdGraph graph, List<Cluster> clusters, bool showAllEdges)
        {
            foreach (var c in clusters)
            {
                var placeholder = c.Placeholder;
                float gridLeft = placeholder.Bounds.X + SpineGutter;
                float spineX = placeholder.Bounds.X + SpineGutter / 2f;

                var rowTops = new List<float>();
                float y = placeholder.Bounds.Y;
                for (int r = 0; r < c.RowHeights.Count; r++)
                {
                    rowTops.Add(y);
                    y += c.RowHeights[r] + RowGap;
                }

                for (int i = 0; i < c.Members.Count; i++)
                {
                    var node = c.Members[i].Node;
                    int row = i / c.Columns;
                    int col = i % c.Columns;
                    node.Bounds = new RectangleF(
                        gridLeft + col * c.CellWidth, rowTops[row],
                        node.Bounds.Width, node.Bounds.Height);
                    node.Rank = placeholder.Rank;
                }

                var anchor = graph[c.AnchorId];
                if (anchor != null) RouteCluster(c, anchor, spineX, rowTops, showAllEdges);
            }
        }

        /// <summary>
        /// Orthogonal bus: a trunk leaves the hub, turns onto the spine beside
        /// the grid, and drops a rail into each row; every satellite then takes
        /// a short vertical stub off its row's rail (which is also where its
        /// relationship label rides). Satellites carrying several lookups to
        /// the same hub show one connector marked "xN" by default, or one
        /// labelled stub per relationship when the caller asks for them all.
        /// </summary>
        private static void RouteCluster(Cluster c, ErdNode anchor, float spineX,
            List<float> rowTops, bool showAllEdges)
        {
            float anchorX = anchor.Bounds.X + anchor.Bounds.Width / 2f;
            float trunkY = c.Below
                ? anchor.Bounds.Bottom + TrunkOffset
                : anchor.Bounds.Y - TrunkOffset;
            float hubY = c.Below ? anchor.Bounds.Bottom : anchor.Bounds.Y;

            for (int i = 0; i < c.Members.Count; i++)
            {
                var member = c.Members[i];
                var node = member.Node;
                int row = i / c.Columns;

                float railY = c.Below
                    ? rowTops[row] - RailInset
                    : rowTops[row] + c.RowHeights[row] + RailInset;
                float memberY = c.Below ? node.Bounds.Y : node.Bounds.Bottom;

                var drawn = showAllEdges ? member.Edges : member.Edges.Take(1).ToList();

                foreach (var e in member.Edges)
                {
                    // Geometry from an earlier layout pass must not leak through.
                    e.IsBack = false;
                    e.LaneY = null;
                    e.RailX = null;
                    e.FromPortX = null;
                    e.ToPortX = null;
                    e.Hidden = !drawn.Contains(e);
                    e.CollapsedCount = 0;
                }

                if (!showAllEdges && member.Edges.Count > 1)
                    drawn[0].CollapsedCount = member.Edges.Count;

                // Parallel stubs fan across the box border so their labels and
                // cardinality glyphs never stack on one point.
                float centerX = node.Bounds.X + node.Bounds.Width / 2f;
                float room = Math.Max(0f, node.Bounds.Width - 28f);
                float pitch = drawn.Count > 1
                    ? Math.Min(StubPitch, room / (drawn.Count - 1))
                    : 0f;

                for (int k = 0; k < drawn.Count; k++)
                {
                    var e = drawn[k];
                    float stubX = centerX + (k - (drawn.Count - 1) / 2f) * pitch;

                    // Built satellite-first, then flipped when the hub is the
                    // relationship's parent, so the "one" tick and the crow's
                    // foot always land on the correct ends.
                    var route = new List<PointF>
                    {
                        new PointF(stubX, memberY),
                        new PointF(stubX, railY),
                        new PointF(spineX, railY),
                        new PointF(spineX, trunkY),
                        new PointF(anchorX, trunkY),
                        new PointF(anchorX, hubY)
                    };

                    bool startsAtSatellite =
                        string.Equals(e.FromId, node.Id, StringComparison.OrdinalIgnoreCase);
                    if (!startsAtSatellite) route.Reverse();

                    e.Route = route;
                    e.LabelAtSource = startsAtSatellite;
                }
            }
        }
    }
}
