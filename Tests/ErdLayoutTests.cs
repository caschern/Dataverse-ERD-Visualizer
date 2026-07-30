using System.Linq;
using DataverseErdVisualizer.Layout;
using DataverseErdVisualizer.Models;
using Xunit;

namespace DataverseErdVisualizer.Tests
{
    public class ErdLayoutTests
    {
        private static ErdGraph Graph(params (string from, string to)[] edges)
        {
            var g = new ErdGraph();
            foreach (var id in edges.SelectMany(e => new[] { e.from, e.to }).Distinct())
            {
                var n = new ErdNode { Id = id, Title = id, Subtitle = id };
                n.Rows.Add(new ErdRow { Name = id + "id", Badge = RowBadge.PrimaryKey });
                g.AddNode(n);
            }
            foreach (var (from, to) in edges)
                g.AddEdge(new ErdEdge { FromId = from, ToId = to, IsSelf = from == to });
            return g;
        }

        private static void SizeAll(ErdGraph g)
            => ErdNodeSizer.Size(g, new FakeSurface());

        [Fact]
        public void Parent_ranks_above_child()
        {
            var g = Graph(("account", "contact"), ("account", "opportunity"));
            SizeAll(g);
            ErdLayoutEngine.Layout(g);

            Assert.True(g["account"].Rank < g["contact"].Rank);
            Assert.True(g["account"].Rank < g["opportunity"].Rank);
            Assert.True(g["account"].Bounds.Bottom <= g["contact"].Bounds.Top);
        }

        [Fact]
        public void Cycle_marks_one_edge_as_back()
        {
            var g = Graph(("account", "contact"), ("contact", "account"));
            SizeAll(g);
            ErdLayoutEngine.Layout(g);

            Assert.Equal(1, g.Edges.Count(e => e.IsBack));
            var back = g.Edges.First(e => e.IsBack);
            Assert.NotNull(back.RailX);
        }

        [Fact]
        public void Self_loop_excluded_from_ranking()
        {
            var g = Graph(("account", "account"), ("account", "contact"));
            SizeAll(g);
            ErdLayoutEngine.Layout(g);

            var self = g.Edges.First(e => e.IsSelf);
            Assert.False(self.IsBack);
            Assert.Equal(0, g["account"].Rank);
            Assert.Equal(1, g["contact"].Rank);
        }

        [Fact]
        public void Canvas_contains_every_node()
        {
            var g = Graph(("a", "b"), ("a", "c"), ("b", "d"), ("c", "d"), ("e", "f"));
            SizeAll(g);
            var canvas = ErdLayoutEngine.Layout(g);

            Assert.True(canvas.Width > 0 && canvas.Height > 0);
            foreach (var n in g.Nodes)
            {
                Assert.True(n.Bounds.X >= 0, n.Id + " has negative X");
                Assert.True(n.Bounds.Y >= 0, n.Id + " has negative Y");
                Assert.True(n.Bounds.Right <= canvas.Width + 0.5f, n.Id + " overflows width");
                Assert.True(n.Bounds.Bottom <= canvas.Height + 0.5f, n.Id + " overflows height");
            }
        }

        [Fact]
        public void Nodes_in_same_rank_do_not_overlap()
        {
            var g = Graph(("hub", "s1"), ("hub", "s2"), ("hub", "s3"), ("hub", "s4"));
            SizeAll(g);
            ErdLayoutEngine.Layout(g);

            var spokes = new[] { g["s1"], g["s2"], g["s3"], g["s4"] }
                .OrderBy(n => n.Bounds.X).ToList();
            for (int i = 1; i < spokes.Count; i++)
                Assert.True(spokes[i - 1].Bounds.Right <= spokes[i].Bounds.X + 0.5f,
                    "spokes overlap");
        }

        [Fact]
        public void Disconnected_nodes_still_get_positions()
        {
            var g = Graph(("a", "b"));
            var lonely = new ErdNode { Id = "island", Title = "island" };
            g.AddNode(lonely);
            SizeAll(g);
            var canvas = ErdLayoutEngine.Layout(g);

            Assert.True(lonely.Bounds.Width > 0);
            Assert.True(lonely.Bounds.Right <= canvas.Width + 0.5f);
        }

        [Fact]
        public void Multi_rank_edge_gets_routed_channel()
        {
            // a → b → c and a direct a → c spanning two ranks.
            var g = Graph(("a", "b"), ("b", "c"), ("a", "c"));
            SizeAll(g);
            ErdLayoutEngine.Layout(g);

            var longEdge = g.Edges.First(e => e.FromId == "a" && e.ToId == "c");
            Assert.False(longEdge.IsBack);
            Assert.Equal(2, g["c"].Rank);
            // Either a routed polyline or (when perfectly aligned) a plain drop.
            if (longEdge.Route != null)
                Assert.True(longEdge.Route.Count >= 2);
        }

        [Fact]
        public void Fan_in_edges_get_distinct_entry_ports()
        {
            var g = Graph(("p1", "child"), ("p2", "child"), ("p3", "child"));
            SizeAll(g);
            ErdLayoutEngine.Layout(g);

            var ports = g.Edges.Select(e => e.ToPortX).ToList();
            Assert.All(ports, p => Assert.NotNull(p));
            Assert.Equal(3, ports.Distinct().Count());

            var child = g["child"];
            Assert.All(ports, p => Assert.InRange(p.Value,
                child.Bounds.X, child.Bounds.Right));
        }

        [Fact]
        public void Fan_out_edges_get_distinct_exit_ports()
        {
            var g = Graph(("hub", "c1"), ("hub", "c2"), ("hub", "c3"), ("hub", "c4"));
            SizeAll(g);
            ErdLayoutEngine.Layout(g);

            var ports = g.Edges.Select(e => e.FromPortX).ToList();
            Assert.Equal(4, ports.Distinct().Count());
        }

        [Fact]
        public void Sizer_reserves_room_for_rows()
        {
            var g = new ErdGraph();
            var n = new ErdNode { Id = "account", Title = "Account", Subtitle = "account" };
            for (int i = 0; i < 5; i++)
                n.Rows.Add(new ErdRow { Name = "column" + i, Type = "Text" });
            g.AddNode(n);
            SizeAll(g);

            Assert.True(n.HeaderHeight > 0);
            Assert.True(n.Bounds.Height >= n.HeaderHeight + 5 * 10);
            Assert.True(n.Bounds.Width >= 150);
        }

        [Fact]
        public void Long_row_text_is_truncated_with_ellipsis()
        {
            var g = new ErdGraph();
            var n = new ErdNode { Id = "x", Title = "X" };
            n.Rows.Add(new ErdRow
            {
                Name = new string('w', 200),
                Type = "Lookup(someverylongentityname)"
            });
            g.AddNode(n);
            SizeAll(g);

            Assert.EndsWith("…", n.Rows[0].DisplayName);
            Assert.True(n.Rows[0].DisplayName.Length < 200);
        }
    }
}
