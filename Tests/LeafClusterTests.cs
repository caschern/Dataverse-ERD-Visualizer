using System;
using System.Collections.Generic;
using System.Linq;
using DataverseErdVisualizer.Layout;
using DataverseErdVisualizer.Models;
using Xunit;

namespace DataverseErdVisualizer.Tests
{
    public class LeafClusterTests
    {
        /// <summary>A hub with the given number of satellite children (plus optional extras).</summary>
        private static ErdGraph HubGraph(int satellites, bool satellitesAreChildren = true,
            params (string from, string to)[] extra)
        {
            var g = new ErdGraph();

            ErdNode Node(string id)
            {
                var existing = g[id];
                if (existing != null) return existing;
                var n = new ErdNode { Id = id, Title = id, Subtitle = id };
                n.Rows.Add(new ErdRow { Name = id + "id", Badge = RowBadge.PrimaryKey });
                return g.AddNode(n);
            }

            Node("hub");
            for (int i = 0; i < satellites; i++)
            {
                // Reverse-ordered ids, so alphabetical sorting is observable.
                var id = "sat" + (satellites - i).ToString("D2");
                Node(id);
                g.AddEdge(satellitesAreChildren
                    ? new ErdEdge { FromId = "hub", ToId = id, Label = id }
                    : new ErdEdge { FromId = id, ToId = "hub", Label = id });
            }
            foreach (var (from, to) in extra)
            {
                Node(from); Node(to);
                g.AddEdge(new ErdEdge { FromId = from, ToId = to });
            }

            ErdNodeSizer.Size(g, new FakeSurface());
            return g;
        }

        [Fact]
        public void Clustering_collapses_a_wide_hub_rank()
        {
            var g = HubGraph(40);
            var wide = LeafClusterLayout.Layout(g, clusterSatellites: false);
            var packed = LeafClusterLayout.Layout(g, clusterSatellites: true);

            Assert.True(packed.Width < wide.Width / 3f,
                $"expected a much narrower canvas, got {packed.Width} vs {wide.Width}");
            Assert.True(packed.Height > wide.Height, "packing trades width for height");
        }

        [Fact]
        public void Small_groups_are_left_alone()
        {
            var g = HubGraph(LeafClusterLayout.MinClusterSize - 1);
            var off = LeafClusterLayout.Layout(g, clusterSatellites: false);
            var on = LeafClusterLayout.Layout(g, clusterSatellites: true);

            Assert.Equal(off.Width, on.Width, 1);
            Assert.Equal(off.Height, on.Height, 1);
        }

        [Fact]
        public void Satellites_are_ordered_alphabetically_across_the_grid()
        {
            var g = HubGraph(12);
            LeafClusterLayout.Layout(g, clusterSatellites: true);

            var satellites = g.Nodes.Where(n => n.Id.StartsWith("sat")).ToList();
            // Reading order: top-to-bottom by row, left-to-right within a row.
            var reading = satellites
                .OrderBy(n => Math.Round(n.Bounds.Y))
                .ThenBy(n => n.Bounds.X)
                .Select(n => n.Id)
                .ToList();

            Assert.Equal(reading.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(), reading);
        }

        [Fact]
        public void Grid_forms_multiple_rows_and_columns()
        {
            var g = HubGraph(30);
            LeafClusterLayout.Layout(g, clusterSatellites: true);

            var satellites = g.Nodes.Where(n => n.Id.StartsWith("sat")).ToList();
            var rows = satellites.Select(n => Math.Round(n.Bounds.Y)).Distinct().Count();
            var cols = satellites.Select(n => Math.Round(n.Bounds.X)).Distinct().Count();

            Assert.True(rows > 1, "expected several rows");
            Assert.True(cols > 1, "expected several columns");

            // Packing is tight: the grid holds everyone, and dropping a row
            // would not (i.e. no row is entirely empty).
            Assert.True(rows * cols >= satellites.Count, "grid too small for its members");
            Assert.True((rows - 1) * cols < satellites.Count, "grid has a wasted row");
        }

        [Fact]
        public void Packed_satellites_do_not_overlap()
        {
            var g = HubGraph(37);
            LeafClusterLayout.Layout(g, clusterSatellites: true);

            var boxes = g.Nodes.Select(n => n.Bounds).ToList();
            for (int i = 0; i < boxes.Count; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                    Assert.False(boxes[i].IntersectsWith(boxes[j]),
                        $"boxes {i} and {j} overlap");
        }

        [Fact]
        public void Child_satellites_sit_below_the_hub_and_parents_above()
        {
            var below = HubGraph(10);
            LeafClusterLayout.Layout(below, clusterSatellites: true);
            var hubBelow = below["hub"];
            Assert.All(below.Nodes.Where(n => n.Id.StartsWith("sat")),
                n => Assert.True(n.Bounds.Y >= hubBelow.Bounds.Bottom));

            var above = HubGraph(10, satellitesAreChildren: false);
            LeafClusterLayout.Layout(above, clusterSatellites: true);
            var hubAbove = above["hub"];
            Assert.All(above.Nodes.Where(n => n.Id.StartsWith("sat")),
                n => Assert.True(n.Bounds.Bottom <= hubAbove.Bounds.Y));
        }

        [Fact]
        public void Cluster_edges_get_bus_routes_anchored_to_their_boxes()
        {
            var g = HubGraph(10);
            LeafClusterLayout.Layout(g, clusterSatellites: true);

            foreach (var e in g.Edges)
            {
                var target = g[e.ToId];
                Assert.NotNull(e.Route);
                Assert.True(e.Route.Count >= 4, "expected an orthogonal bus polyline");
                // Last point lands on the target box's top border, centered.
                var last = e.Route[e.Route.Count - 1];
                Assert.Equal(target.Bounds.Y, last.Y, 1);
                Assert.InRange(last.X, target.Bounds.X, target.Bounds.Right);
            }
        }

        [Fact]
        public void Upward_cluster_edges_label_at_their_source()
        {
            var g = HubGraph(8, satellitesAreChildren: false);
            LeafClusterLayout.Layout(g, clusterSatellites: true);

            Assert.All(g.Edges, e => Assert.True(e.LabelAtSource));

            var down = HubGraph(8);
            LeafClusterLayout.Layout(down, clusterSatellites: true);
            Assert.All(down.Edges, e => Assert.False(e.LabelAtSource));
        }

        [Fact]
        public void Tables_with_other_relationships_are_not_clustered()
        {
            // sat01 also links to "other", so it is not a satellite any more.
            var g = HubGraph(8, true, ("sat01", "other"));
            LeafClusterLayout.Layout(g, clusterSatellites: true);

            var clustered = LeafClusterLayout.CountClusteredTables(g);
            Assert.Equal(7, clustered);
        }

        [Fact]
        public void Self_referencing_tables_are_never_clustered()
        {
            var g = HubGraph(8);
            var sat = g["sat01"];
            g.AddEdge(new ErdEdge { FromId = sat.Id, ToId = sat.Id, IsSelf = true });
            ErdNodeSizer.Size(g, new FakeSurface());

            Assert.Equal(7, LeafClusterLayout.CountClusteredTables(g));
        }

        [Fact]
        public void Graph_without_hubs_is_unchanged()
        {
            var g = new ErdGraph();
            foreach (var id in new[] { "a", "b", "c" })
                g.AddNode(new ErdNode { Id = id, Title = id });
            g.AddEdge(new ErdEdge { FromId = "a", ToId = "b" });
            g.AddEdge(new ErdEdge { FromId = "b", ToId = "c" });
            ErdNodeSizer.Size(g, new FakeSurface());

            var off = LeafClusterLayout.Layout(g, clusterSatellites: false);
            var on = LeafClusterLayout.Layout(g, clusterSatellites: true);
            Assert.Equal(off.Width, on.Width, 1);
        }
    }
}
