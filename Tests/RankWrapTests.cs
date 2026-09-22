using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using DataverseErdVisualizer.Layout;
using DataverseErdVisualizer.Models;
using Xunit;

namespace DataverseErdVisualizer.Tests
{
    public class RankWrapTests
    {
        private static ErdGraph Graph(bool wrap, params (string from, string to)[] edges)
        {
            var g = new ErdGraph { WrapWideRanks = wrap };
            foreach (var id in edges.SelectMany(e => new[] { e.from, e.to }).Distinct())
                AddNode(g, id);
            foreach (var (from, to) in edges)
                g.AddEdge(new ErdEdge { FromId = from, ToId = to });
            return g;
        }

        private static ErdNode AddNode(ErdGraph g, string id)
        {
            var existing = g[id];
            if (existing != null) return existing;
            var n = new ErdNode { Id = id, Title = id, Subtitle = id };
            n.Rows.Add(new ErdRow { Name = id + "id", Badge = RowBadge.PrimaryKey });
            return g.AddNode(n);
        }

        /// <summary>
        /// Two hubs and many tables that each belong to BOTH of them — so not
        /// satellites, which is exactly the width clustering cannot remove.
        /// </summary>
        private static ErdGraph SharedChildren(bool wrap, int count)
        {
            var edges = new List<(string, string)>();
            for (int i = 0; i < count; i++)
            {
                edges.Add(("hub1", "t" + i.ToString("D2")));
                edges.Add(("hub2", "t" + i.ToString("D2")));
            }
            var g = Graph(wrap, edges.ToArray());
            ErdNodeSizer.Size(g, new FakeSurface());
            return g;
        }

        private static float WidestRank(ErdGraph g)
            => g.Nodes.GroupBy(n => Math.Round(n.Bounds.Y))
                .Max(r => r.Max(n => n.Bounds.Right) - r.Min(n => n.Bounds.X));

        [Fact]
        public void A_rank_of_interconnected_tables_is_wrapped()
        {
            var off = SharedChildren(wrap: false, count: 40);
            var on = SharedChildren(wrap: true, count: 40);

            var wide = ErdLayoutEngine.Layout(off);
            var wrapped = ErdLayoutEngine.Layout(on);

            Assert.True(wrapped.Width < wide.Width / 2f,
                $"expected a much narrower canvas, got {wrapped.Width} vs {wide.Width}");
            Assert.True(WidestRank(on) < WidestRank(off));
            Assert.True(on.Nodes.Select(n => n.Rank).Distinct().Count() >
                        off.Nodes.Select(n => n.Rank).Distinct().Count());
        }

        [Fact]
        public void Parents_stay_above_children_after_wrapping()
        {
            var g = SharedChildren(wrap: true, count: 40);
            ErdLayoutEngine.Layout(g);

            foreach (var e in g.Edges.Where(e => !e.IsBack && !e.IsSelf))
            {
                var from = g[e.FromId];
                var to = g[e.ToId];
                Assert.True(to.Rank > from.Rank, $"{e.FromId} -> {e.ToId} is not layered");
                Assert.True(to.Bounds.Y >= from.Bounds.Bottom, $"{e.ToId} is not below {e.FromId}");
            }
        }

        [Fact]
        public void Wrapped_tables_never_overlap()
        {
            var g = SharedChildren(wrap: true, count: 40);
            ErdLayoutEngine.Layout(g);

            var boxes = g.Nodes.Select(n => n.Bounds).ToList();
            for (int i = 0; i < boxes.Count; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                    Assert.False(boxes[i].IntersectsWith(boxes[j]), $"boxes {i} and {j} overlap");
        }

        [Fact]
        public void Multi_rank_edges_made_by_wrapping_are_routed_through_channels()
        {
            // A table moved down a rank is two ranks below its parents, and
            // that edge must be routed like any other multi-rank edge.
            var g = SharedChildren(wrap: true, count: 40);
            ErdLayoutEngine.Layout(g);

            var lengthened = g.Edges.Where(e => g[e.ToId].Rank - g[e.FromId].Rank > 1).ToList();
            Assert.NotEmpty(lengthened);
            Assert.All(lengthened, e => Assert.NotNull(e.Route));
        }

        [Fact]
        public void Small_diagrams_are_left_exactly_as_they_were()
        {
            var off = Graph(false, ("a", "b"), ("a", "c"), ("a", "d"), ("b", "e"));
            var on = Graph(true, ("a", "b"), ("a", "c"), ("a", "d"), ("b", "e"));
            ErdNodeSizer.Size(off, new FakeSurface());
            ErdNodeSizer.Size(on, new FakeSurface());

            var sizeOff = ErdLayoutEngine.Layout(off);
            var sizeOn = ErdLayoutEngine.Layout(on);

            Assert.Equal(sizeOff, sizeOn);
            foreach (var n in off.Nodes)
                Assert.Equal(n.Bounds, on[n.Id].Bounds);
        }

        [Fact]
        public void Unrelated_tables_are_packed_into_rows_instead_of_one_line()
        {
            var g = new ErdGraph { WrapWideRanks = true };
            for (int i = 0; i < 60; i++) AddNode(g, "island" + i.ToString("D2"));
            ErdNodeSizer.Size(g, new FakeSurface());

            var canvas = ErdLayoutEngine.Layout(g);

            Assert.True(g.Nodes.Select(n => n.Rank).Distinct().Count() > 1);
            Assert.True(canvas.Width < canvas.Height * 4f, $"still a ribbon: {canvas.Width}x{canvas.Height}");
        }

        // ------------------------------------------------ satellite grids

        /// <summary>Axis-aligned segments of routed edges that pass through a box they do not belong to.</summary>
        private static List<string> RoutesThroughBoxes(ErdGraph g)
        {
            var bad = new List<string>();
            foreach (var e in g.Edges.Where(x => x.Route != null && !x.Hidden))
            {
                for (int i = 0; i < e.Route.Count - 1; i++)
                {
                    var a = e.Route[i];
                    var b = e.Route[i + 1];
                    var segment = RectangleF.FromLTRB(
                        Math.Min(a.X, b.X) - 0.5f, Math.Min(a.Y, b.Y) - 0.5f,
                        Math.Max(a.X, b.X) + 0.5f, Math.Max(a.Y, b.Y) + 0.5f);

                    foreach (var n in g.Nodes)
                    {
                        if (n.Id == e.FromId || n.Id == e.ToId) continue;
                        var box = n.Bounds;
                        box.Inflate(-2f, -2f);
                        if (!box.IntersectsWith(segment)) continue;
                        bad.Add($"{e.FromId}->{e.ToId} crosses {n.Id}");
                        break;
                    }
                }
            }
            return bad;
        }

        /// <summary>
        /// A hub buried three ranks deep under a broad band of other tables,
        /// with a grid of reference tables that belong ABOVE it.
        /// </summary>
        private static ErdGraph DeepHubWithParentGrid(bool wrap)
        {
            var edges = new List<(string, string)>();
            for (int i = 0; i < 6; i++)
            {
                edges.Add(("root", "band1_" + i));
                edges.Add(("band1_" + i, "band2_" + i));
            }
            edges.Add(("band2_0", "hub"));
            for (int i = 0; i < 8; i++)
                edges.Add(("lookup" + i, "hub"));   // satellites that are parents of hub

            var g = Graph(wrap, edges.ToArray());
            ErdNodeSizer.Size(g, new FakeSurface());
            return g;
        }

        [Fact]
        public void A_grid_above_a_deep_hub_sits_right_above_it()
        {
            var g = DeepHubWithParentGrid(wrap: false);
            LeafClusterLayout.Layout(g, clusterSatellites: true);

            var hub = g["hub"];
            var grid = g.Nodes.Where(n => n.Id.StartsWith("lookup")).ToList();
            Assert.All(grid, n => Assert.True(n.Bounds.Bottom <= hub.Bounds.Y, $"{n.Id} not above hub"));

            // Nothing may sit between the grid and its hub: the bus runs straight.
            var gridBottom = grid.Max(n => n.Bounds.Bottom);
            var between = g.Nodes.Where(n =>
                    !n.Id.StartsWith("lookup") && n.Id != "hub" &&
                    n.Bounds.Y >= gridBottom && n.Bounds.Bottom <= hub.Bounds.Y)
                .Select(n => n.Id).ToList();
            Assert.Empty(between);
        }

        /// <summary>
        /// Regression guard for every routed connector — satellite buses and
        /// the bundled trunks that wrapping creates. (The adjacency test above
        /// is what catches a grid drifting away from its hub; this one catches
        /// any route, however produced, that cuts across a box.)
        /// </summary>
        [Fact]
        public void No_routed_connector_cuts_through_a_box()
        {
            foreach (var wrap in new[] { false, true })
            {
                var deep = DeepHubWithParentGrid(wrap);
                LeafClusterLayout.Layout(deep, clusterSatellites: true);
                var crossings = RoutesThroughBoxes(deep);
                Assert.True(crossings.Count == 0,
                    $"deep hub, wrap={wrap}: " + string.Join("; ", crossings.Take(5)));
            }

            var shared = SharedChildren(wrap: true, count: 40);
            ErdLayoutEngine.Layout(shared);
            var trunkCrossings = RoutesThroughBoxes(shared);
            Assert.True(trunkCrossings.Count == 0,
                "bundled trunks: " + string.Join("; ", trunkCrossings.Take(5)));
        }

        [Fact]
        public void Long_edges_from_one_table_share_a_trunk()
        {
            // 32 tables pushed down under two hubs: without bundling each edge
            // reserved its own channel through every rank it crossed.
            var g = SharedChildren(wrap: true, count: 40);
            ErdLayoutEngine.Layout(g);

            var fromHub1 = g.Edges.Where(e => e.FromId == "hub1" && e.Route != null).ToList();
            Assert.True(fromHub1.Count > 1);

            // Each edge still leaves the hub from its own port (that keeps the
            // crow's feet apart), but every one of them crosses the next row
            // at the same X: one trunk, not one channel per edge.
            var nextRow = g.Nodes.Where(n => n.Rank == g["hub1"].Rank + 1).ToList();
            float bandY = nextRow.Average(n => n.Bounds.Y + n.Bounds.Height / 2f);

            var trunkXs = fromHub1
                .Select(e => VerticalCrossingX(e.Route, bandY))
                .Where(x => x.HasValue)
                .Select(x => Math.Round(x.Value))
                .Distinct()
                .ToList();

            Assert.NotEmpty(trunkXs);
            Assert.Single(trunkXs);
        }

        [Fact]
        public void A_trunk_leaves_its_table_through_one_port()
        {
            var g = SharedChildren(wrap: true, count: 40);
            ErdLayoutEngine.Layout(g);

            var hub1 = g["hub1"];
            var outgoing = g.Edges.Where(e => e.FromId == "hub1").ToList();
            var bundled = outgoing.Where(e => g[e.ToId].Rank - hub1.Rank > 1).ToList();
            var direct = outgoing.Where(e => g[e.ToId].Rank - hub1.Rank == 1).ToList();
            Assert.NotEmpty(bundled);
            Assert.NotEmpty(direct);

            // Everything riding the trunk exits at the same point...
            Assert.Single(bundled.Select(e => Math.Round(e.FromPortX.Value)).Distinct());

            // ...while edges to the row just below keep a port each, and none of
            // them lands on the trunk's.
            var trunkPort = Math.Round(bundled[0].FromPortX.Value);
            var directPorts = direct.Select(e => Math.Round(e.FromPortX.Value)).ToList();
            Assert.Equal(direct.Count, directPorts.Distinct().Count());
            Assert.DoesNotContain(trunkPort, directPorts);
        }

        [Fact]
        public void Wrapped_rows_read_in_alphabetical_order_from_the_top()
        {
            var g = SharedChildren(wrap: true, count: 40);
            ErdLayoutEngine.Layout(g);

            var rowOf = g.Nodes.Where(n => n.Id.StartsWith("t"))
                .ToDictionary(n => n.Id, n => n.Rank);

            // Every table sits in the same row as, or a row above, every table
            // that sorts after it.
            var ids = rowOf.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            for (int i = 1; i < ids.Count; i++)
                Assert.True(rowOf[ids[i - 1]] <= rowOf[ids[i]],
                    $"{ids[i - 1]} (row {rowOf[ids[i - 1]]}) is below {ids[i]} (row {rowOf[ids[i]]})");

            Assert.True(rowOf["t00"] < rowOf["t39"]);
        }

        /// <summary>X of the vertical segment of a route that passes through the given Y.</summary>
        private static float? VerticalCrossingX(List<PointF> route, float y)
        {
            for (int i = 0; i < route.Count - 1; i++)
            {
                var a = route[i];
                var b = route[i + 1];
                if (Math.Abs(a.X - b.X) > 0.5f) continue;
                if (y >= Math.Min(a.Y, b.Y) && y <= Math.Max(a.Y, b.Y)) return a.X;
            }
            return null;
        }
    }
}
