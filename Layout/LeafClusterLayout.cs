using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using DataverseErdVisualizer.Models;

namespace DataverseErdVisualizer.Layout
{
    /// <summary>
    /// Packs "satellite" tables — those whose only relationship in the diagram
    /// is to a single hub — into a compact grid beside that hub, instead of
    /// letting a hub's satellites stretch one rank across tens of thousands of
    /// pixels (a Contact-style hub with 148 satellites produced a 31,000 x 650
    /// ribbon before this).
    ///
    /// A satellite's horizontal position carries no information: it has exactly
    /// one connection, so it can sit anywhere without hiding structure. Two
    /// kinds of cluster form, both common in Dataverse schemas:
    ///   • children  — many tables carrying a lookup TO the hub → grid below it
    ///   • parents   — the hub's own lookup/reference tables → grid above it
    ///
    /// The pack runs as a pre-layout transform: satellites are replaced by one
    /// placeholder box per cluster, the normal layered layout runs untouched on
    /// that reduced graph, then each placeholder is expanded into its grid and
    /// the satellite connectors are routed as an orthogonal bus (trunk from the
    /// hub, spine down the side, one rail per row, a short drop into each box).
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

        public static SizeF Layout(ErdGraph graph, bool clusterSatellites)
        {
            if (!clusterSatellites) return ErdLayoutEngine.Layout(graph);

            var clusters = FindClusters(graph);
            if (clusters.Count == 0) return ErdLayoutEngine.Layout(graph);

            var reduced = BuildReducedGraph(graph, clusters);
            var canvas = ErdLayoutEngine.Layout(reduced);
            Expand(graph, clusters);
            return canvas;
        }

        /// <summary>Number of tables packed into grids (diagnostics/tests).</summary>
        public static int CountClusteredTables(ErdGraph graph)
            => FindClusters(graph).Sum(c => c.Members.Count);

        private class Cluster
        {
            public string AnchorId;
            public bool Below;              // grid sits below the hub
            public List<ErdNode> Members = new List<ErdNode>();
            public List<ErdEdge> Edges = new List<ErdEdge>();
            public ErdNode Placeholder;
            public int Columns = 1;
            public float CellWidth;
            public List<float> RowHeights = new List<float>();
            public SizeF Size;
        }

        // ------------------------------------------------------------ detect

        private static List<Cluster> FindClusters(ErdGraph graph)
        {
            var degree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var selfLooped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in graph.Nodes) degree[n.Id] = 0;

            foreach (var e in graph.Edges)
            {
                if (e.IsSelf) { selfLooped.Add(e.FromId); continue; }
                if (degree.ContainsKey(e.FromId)) degree[e.FromId]++;
                if (degree.ContainsKey(e.ToId)) degree[e.ToId]++;
            }

            var groups = new Dictionary<string, Cluster>();
            foreach (var edge in graph.Edges)
            {
                if (edge.IsSelf) continue;

                foreach (var memberId in new[] { edge.FromId, edge.ToId })
                {
                    int d;
                    if (!degree.TryGetValue(memberId, out d) || d != 1) continue;
                    if (selfLooped.Contains(memberId)) continue;

                    var member = graph[memberId];
                    if (member == null) continue;

                    bool memberIsChild = string.Equals(edge.ToId, memberId, StringComparison.OrdinalIgnoreCase);
                    var anchorId = memberIsChild ? edge.FromId : edge.ToId;
                    if (string.Equals(anchorId, memberId, StringComparison.OrdinalIgnoreCase)) continue;
                    if (graph[anchorId] == null) continue;

                    // A two-table component is not a hub; leave it alone.
                    int anchorDegree;
                    if (!degree.TryGetValue(anchorId, out anchorDegree) || anchorDegree <= 1) continue;

                    var key = anchorId.ToLowerInvariant() + (memberIsChild ? "|below" : "|above");
                    Cluster cluster;
                    if (!groups.TryGetValue(key, out cluster))
                        groups[key] = cluster = new Cluster { AnchorId = anchorId, Below = memberIsChild };
                    cluster.Members.Add(member);
                    cluster.Edges.Add(edge);
                }
            }

            var result = groups.Values.Where(c => c.Members.Count >= MinClusterSize).ToList();
            foreach (var c in result) SortAndMeasure(c);
            return result;
        }

        /// <summary>
        /// Orders satellites alphabetically (a 100-box grid is scanned to FIND a
        /// table, and relationship type is already visible on the edges), then
        /// picks the column count whose grid comes closest to the target aspect.
        /// </summary>
        private static void SortAndMeasure(Cluster c)
        {
            var pairs = c.Members
                .Select((m, i) => new { Member = m, Edge = c.Edges[i] })
                .OrderBy(p => p.Member.Title ?? p.Member.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Member.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
            c.Members = pairs.Select(p => p.Member).ToList();
            c.Edges = pairs.Select(p => p.Edge).ToList();

            int n = c.Members.Count;
            c.CellWidth = c.Members.Max(m => m.Bounds.Width) + ColumnGap;
            float rowPitch = c.Members.Average(m => m.Bounds.Height) + RowGap;

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
                    max = Math.Max(max, c.Members[i].Bounds.Height);
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
                clusters.SelectMany(c => c.Members).Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
            var clusteredEdges = new HashSet<ErdEdge>(clusters.SelectMany(c => c.Edges));

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
                if (clusteredEdges.Contains(e)) continue;
                if (memberIds.Contains(e.FromId) || memberIds.Contains(e.ToId)) continue;
                reduced.AddEdge(e);
            }

            return reduced;
        }

        // ------------------------------------------------------------ expand

        private static void Expand(ErdGraph graph, List<Cluster> clusters)
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
                    var m = c.Members[i];
                    int row = i / c.Columns;
                    int col = i % c.Columns;
                    m.Bounds = new RectangleF(
                        gridLeft + col * c.CellWidth, rowTops[row],
                        m.Bounds.Width, m.Bounds.Height);
                    m.Rank = placeholder.Rank;
                }

                var anchor = graph[c.AnchorId];
                if (anchor != null) RouteCluster(c, anchor, spineX, rowTops);
            }
        }

        /// <summary>
        /// Orthogonal bus: a trunk leaves the hub, turns onto the spine beside
        /// the grid, and drops a rail into each row; every satellite then takes
        /// a short vertical stub off its row's rail (which is also where its
        /// rotated label rides).
        /// </summary>
        private static void RouteCluster(Cluster c, ErdNode anchor, float spineX, List<float> rowTops)
        {
            float anchorX = anchor.Bounds.X + anchor.Bounds.Width / 2f;
            float trunkY = c.Below
                ? anchor.Bounds.Bottom + TrunkOffset
                : anchor.Bounds.Y - TrunkOffset;

            for (int i = 0; i < c.Members.Count; i++)
            {
                var m = c.Members[i];
                var e = c.Edges[i];
                int row = i / c.Columns;

                // Geometry from an earlier layout pass must not leak through.
                e.IsBack = false;
                e.LaneY = null;
                e.RailX = null;
                e.FromPortX = null;
                e.ToPortX = null;

                float cx = m.Bounds.X + m.Bounds.Width / 2f;

                if (c.Below)
                {
                    float railY = rowTops[row] - RailInset;
                    e.LabelAtSource = false;
                    e.Route = new List<PointF>
                    {
                        new PointF(anchorX, anchor.Bounds.Bottom),
                        new PointF(anchorX, trunkY),
                        new PointF(spineX, trunkY),
                        new PointF(spineX, railY),
                        new PointF(cx, railY),
                        new PointF(cx, m.Bounds.Y)
                    };
                }
                else
                {
                    // Satellite is the parent: the connector runs upward into
                    // the hub, so the label rides the satellite's own stub.
                    float railY = rowTops[row] + c.RowHeights[row] + RailInset;
                    e.LabelAtSource = true;
                    e.Route = new List<PointF>
                    {
                        new PointF(cx, m.Bounds.Bottom),
                        new PointF(cx, railY),
                        new PointF(spineX, railY),
                        new PointF(spineX, trunkY),
                        new PointF(anchorX, trunkY),
                        new PointF(anchorX, anchor.Bounds.Y)
                    };
                }
            }
        }
    }
}
