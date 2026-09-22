using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using DataverseErdVisualizer.Layout;
using DataverseErdVisualizer.Models;
using Xunit;

namespace DataverseErdVisualizer.Tests
{
    public class PinnedLayoutTests
    {
        private static ErdDiagram Diagram(params (string from, string to)[] edges)
        {
            var g = new ErdGraph();
            foreach (var id in edges.SelectMany(e => new[] { e.from, e.to }).Distinct())
            {
                var n = new ErdNode { Id = id, Title = id, Subtitle = id };
                n.Rows.Add(new ErdRow { Name = id + "id", Badge = RowBadge.PrimaryKey });
                g.AddNode(n);
            }
            foreach (var (from, to) in edges)
                g.AddEdge(new ErdEdge { FromId = from, ToId = to, Label = from + "-" + to });

            ErdNodeSizer.Size(g, new FakeSurface());
            var canvas = LeafClusterLayout.Layout(g, clusterSatellites: true);
            return new ErdDiagram { Graph = g, CanvasSize = canvas };
        }

        [Fact]
        public void Pinned_tables_go_back_to_their_stored_position()
        {
            var diagram = Diagram(("a", "b"), ("a", "c"));
            var pins = new Dictionary<string, PointF> { ["b"] = new PointF(900f, 700f) };

            PinnedLayout.Apply(diagram, pins);

            var b = diagram.Graph["b"];
            Assert.Equal(900f, b.Bounds.X, 1);
            Assert.Equal(700f, b.Bounds.Y, 1);
            Assert.True(b.Pinned);

            // Tables nobody moved keep whatever the layout engine chose.
            Assert.False(diagram.Graph["c"].Pinned);
        }

        [Fact]
        public void Canvas_grows_to_contain_a_table_moved_beyond_it()
        {
            var diagram = Diagram(("a", "b"));
            var before = diagram.CanvasSize;

            PinnedLayout.Apply(diagram, new Dictionary<string, PointF>
            {
                ["b"] = new PointF(before.Width + 500f, before.Height + 400f)
            });

            var b = diagram.Graph["b"];
            Assert.True(diagram.CanvasSize.Width >= b.Bounds.Right);
            Assert.True(diagram.CanvasSize.Height >= b.Bounds.Bottom);
        }

        [Fact]
        public void Routing_is_cleared_only_for_edges_touching_a_moved_table()
        {
            var diagram = Diagram(("hub", "x"), ("hub", "y"), ("far1", "far2"));
            foreach (var e in diagram.Graph.Edges)
            {
                e.Route = new List<PointF> { new PointF(1, 2), new PointF(3, 4) };
                e.LaneY = 5f;
                e.FromPortX = 6f;
                e.ToPortX = 7f;
            }

            PinnedLayout.Apply(diagram, new Dictionary<string, PointF> { ["x"] = new PointF(10f, 900f) });

            // The edge into the moved table was routed for its old position.
            var touched = diagram.Graph.Edges.Single(e => e.ToId == "x");
            Assert.Null(touched.Route);
            Assert.Null(touched.LaneY);
            Assert.Null(touched.ToPortX);
            Assert.Equal(6f, touched.FromPortX);   // the other end did not move

            // An edge nowhere near it keeps the routing the engine computed.
            var untouched = diagram.Graph.Edges.Single(e => e.FromId == "far1");
            Assert.NotNull(untouched.Route);
            Assert.Equal(5f, untouched.LaneY);
        }

        [Fact]
        public void Pins_for_tables_not_in_the_diagram_are_ignored_not_lost()
        {
            var diagram = Diagram(("a", "b"));
            var pins = new Dictionary<string, PointF>
            {
                ["b"] = new PointF(400f, 400f),
                ["unticked"] = new PointF(50f, 50f)
            };

            PinnedLayout.Apply(diagram, pins);

            Assert.Equal(2, pins.Count);          // kept for when that table returns
            Assert.Null(diagram.Graph["unticked"]);
        }

        [Fact]
        public void Negative_positions_are_clamped_into_the_canvas()
        {
            var diagram = Diagram(("a", "b"));

            PinnedLayout.Apply(diagram, new Dictionary<string, PointF>
            {
                ["b"] = new PointF(-500f, -200f)
            });

            Assert.Equal(0f, diagram.Graph["b"].Bounds.X, 1);
            Assert.Equal(0f, diagram.Graph["b"].Bounds.Y, 1);
        }

        [Fact]
        public void Collect_returns_only_hand_placed_tables()
        {
            var diagram = Diagram(("a", "b"), ("a", "c"));
            diagram.Graph["b"].Pinned = true;
            diagram.Graph["b"].Bounds = new RectangleF(120f, 340f, 10f, 10f);

            var collected = PinnedLayout.Collect(diagram);

            Assert.Equal(new[] { "b" }, collected.Keys.ToArray());
            Assert.Equal(new PointF(120f, 340f), collected["b"]);
        }

        [Fact]
        public void Nothing_pinned_leaves_the_diagram_untouched()
        {
            var diagram = Diagram(("a", "b"));
            var before = diagram.Graph["b"].Bounds;

            PinnedLayout.Apply(diagram, new Dictionary<string, PointF>());
            PinnedLayout.Apply(diagram, null);

            Assert.Equal(before, diagram.Graph["b"].Bounds);
        }

        // ------------------------------------------------------------- store

        [Fact]
        public void Positions_survive_a_save_and_load()
        {
            var key = "test-solution-" + Guid.NewGuid().ToString("N");
            try
            {
                LayoutStore.Save(key, new Dictionary<string, PointF>
                {
                    ["jn_case"] = new PointF(120.5f, 340.25f),
                    ["contact"] = new PointF(0f, 12f)
                });

                var loaded = LayoutStore.Load(key);

                Assert.Equal(2, loaded.Count);
                Assert.Equal(120.5f, loaded["jn_case"].X, 2);
                Assert.Equal(340.25f, loaded["jn_case"].Y, 2);
                Assert.Equal(12f, loaded["contact"].Y, 2);
            }
            finally
            {
                LayoutStore.Delete(key);
            }
        }

        [Fact]
        public void Saving_an_empty_arrangement_removes_the_stored_one()
        {
            var key = "test-solution-" + Guid.NewGuid().ToString("N");
            try
            {
                LayoutStore.Save(key, new Dictionary<string, PointF> { ["a"] = new PointF(1f, 2f) });
                Assert.Single(LayoutStore.Load(key));

                LayoutStore.Save(key, new Dictionary<string, PointF>());

                Assert.Empty(LayoutStore.Load(key));
            }
            finally
            {
                LayoutStore.Delete(key);
            }
        }

        [Fact]
        public void An_unknown_solution_simply_has_no_pins()
        {
            Assert.Empty(LayoutStore.Load("never-saved-" + Guid.NewGuid().ToString("N")));
            Assert.Empty(LayoutStore.Load(null));
            Assert.Empty(LayoutStore.Load("   "));
        }

        [Fact]
        public void A_damaged_layout_file_costs_the_pins_not_the_tool()
        {
            var key = "test-solution-" + Guid.NewGuid().ToString("N");
            try
            {
                LayoutStore.Save(key, new Dictionary<string, PointF> { ["a"] = new PointF(1f, 2f) });

                var path = Directory.GetFiles(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "MscrmTools", "XrmToolBox", "Settings", "DataverseErdVisualizer", "layouts"),
                    key + ".layout").Single();
                File.WriteAllText(path, "this is not a layout\nnor\tis\tthis\tline\n\t\t\n");

                // Garbage in, no pins out — and no exception.
                Assert.Empty(LayoutStore.Load(key));
            }
            finally
            {
                LayoutStore.Delete(key);
            }
        }
    }
}
