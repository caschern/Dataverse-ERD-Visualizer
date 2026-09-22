using System.Linq;
using DataverseErdVisualizer;
using DataverseErdVisualizer.Models;
using Xunit;

namespace DataverseErdVisualizer.Tests
{
    public class NeighbourhoodTests
    {
        /// <summary>
        /// A chain (a → b → c → d), a second table hanging off the seed, an
        /// unrelated island, and a system relationship.
        /// </summary>
        private static ErdModel Model()
        {
            var model = new ErdModel { Solution = new SolutionInfo { FriendlyName = "Test" } };

            foreach (var name in new[] { "a", "b", "c", "d", "side", "island", "systemuser" })
                model.Entities.Add(new EntityModel
                {
                    LogicalName = name,
                    DisplayName = name,
                    PrimaryIdAttribute = name + "id"
                });

            void Rel(string one, string many, string lookup)
                => model.Relationships.Add(new RelationshipModel
                {
                    SchemaName = one + "_" + many,
                    Kind = RelationshipKind.OneToMany,
                    ReferencedEntity = one,
                    ReferencingEntity = many,
                    LookupAttribute = lookup
                });

            Rel("a", "b", "aid");
            Rel("b", "c", "bid");
            Rel("c", "d", "cid");
            Rel("a", "side", "aid");
            Rel("systemuser", "a", "ownerid");      // system noise
            Rel("a", "a", "parentaid");             // self-referential
            return model;
        }

        [Fact]
        public void One_hop_returns_the_table_and_its_direct_relations()
        {
            var set = ErdGraphBuilder.Neighbourhood(Model(), new ErdOptions(), "a", 1);

            Assert.Equal(new[] { "a", "b", "side" }, set.OrderBy(x => x).ToArray());
        }

        [Fact]
        public void Two_hops_reach_one_table_further_out()
        {
            var set = ErdGraphBuilder.Neighbourhood(Model(), new ErdOptions(), "a", 2);

            Assert.Contains("c", set);
            Assert.DoesNotContain("d", set);        // three hops away
            Assert.DoesNotContain("island", set);   // unrelated
        }

        [Fact]
        public void System_relationships_are_followed_only_when_they_are_shown()
        {
            var hidden = ErdGraphBuilder.Neighbourhood(Model(), new ErdOptions(), "a", 1);
            Assert.DoesNotContain("systemuser", hidden);

            var shown = ErdGraphBuilder.Neighbourhood(Model(),
                new ErdOptions { IncludeSystemRelationships = true }, "a", 1);
            Assert.Contains("systemuser", shown);
        }

        [Fact]
        public void Many_to_many_links_are_followed_only_when_they_are_shown()
        {
            var model = Model();
            model.Relationships.Add(new RelationshipModel
            {
                SchemaName = "a_island",
                Kind = RelationshipKind.ManyToMany,
                ReferencedEntity = "a",
                ReferencingEntity = "island",
                IntersectEntity = "a_island_intersect"
            });

            Assert.Contains("island", ErdGraphBuilder.Neighbourhood(model, new ErdOptions(), "a", 1));
            Assert.DoesNotContain("island", ErdGraphBuilder.Neighbourhood(model,
                new ErdOptions { IncludeManyToMany = false }, "a", 1));
        }

        [Fact]
        public void A_table_with_no_relationships_focuses_to_itself()
        {
            var set = ErdGraphBuilder.Neighbourhood(Model(), new ErdOptions(), "island", 2);

            Assert.Equal(new[] { "island" }, set.ToArray());
        }

        [Fact]
        public void Relationships_are_followed_in_both_directions()
        {
            // Seeded from the child end, the parent must still come back.
            var set = ErdGraphBuilder.Neighbourhood(Model(), new ErdOptions(), "d", 1);

            Assert.Equal(new[] { "c", "d" }, set.OrderBy(x => x).ToArray());
        }

        [Fact]
        public void Zero_hops_is_just_the_table_itself()
        {
            Assert.Equal(new[] { "a" },
                ErdGraphBuilder.Neighbourhood(Model(), new ErdOptions(), "a", 0).ToArray());
        }

        [Fact]
        public void Missing_input_yields_an_empty_set_rather_than_throwing()
        {
            Assert.Empty(ErdGraphBuilder.Neighbourhood(null, new ErdOptions(), "a", 1));
            Assert.Empty(ErdGraphBuilder.Neighbourhood(Model(), new ErdOptions(), null, 1));
            Assert.Empty(ErdGraphBuilder.Neighbourhood(Model(), new ErdOptions(), "", 1));
        }

        [Fact]
        public void A_focused_set_drives_the_diagram_it_is_handed_to()
        {
            var model = Model();
            var focus = ErdGraphBuilder.Neighbourhood(model, new ErdOptions(), "a", 1);

            var graph = ErdGraphBuilder.BuildGraph(model, new ErdOptions
            {
                SelectedEntities = focus,
                IncludeExternalEntities = false
            });

            Assert.Equal(new[] { "a", "b", "side" },
                graph.Nodes.Select(n => n.Id).OrderBy(x => x).ToArray());
        }
    }
}
