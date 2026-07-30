using System.Collections.Generic;
using System.Linq;
using DataverseErdVisualizer;
using DataverseErdVisualizer.Models;
using Xunit;

namespace DataverseErdVisualizer.Tests
{
    public class ErdGraphBuilderTests
    {
        // ------------------------------------------------------------ helpers

        private static EntityModel Entity(string logical, bool custom = false,
            bool intersect = false, bool external = false)
        {
            var e = new EntityModel
            {
                LogicalName = logical,
                DisplayName = char.ToUpper(logical[0]) + logical.Substring(1),
                PrimaryIdAttribute = logical + "id",
                PrimaryNameAttribute = "name",
                IsCustom = custom,
                IsIntersect = intersect,
                IsExternal = external
            };
            e.Attributes.Add(new AttributeModel
            {
                LogicalName = logical + "id",
                DisplayName = logical + "id",
                IsPrimaryId = true,
                TypeLabel = "GUID"
            });
            e.Attributes.Add(new AttributeModel
            {
                LogicalName = "name",
                DisplayName = "Name",
                IsPrimaryName = true,
                TypeLabel = "Text"
            });
            return e;
        }

        private static AttributeModel Lookup(string logical, string target, bool custom = true)
        {
            var a = new AttributeModel
            {
                LogicalName = logical,
                DisplayName = logical,
                IsLookup = true,
                IsCustom = custom,
                TypeLabel = "Lookup(" + target + ")"
            };
            a.Targets.Add(target);
            return a;
        }

        private static RelationshipModel OneToMany(string schema, string one, string many, string lookup)
            => new RelationshipModel
            {
                SchemaName = schema,
                Kind = RelationshipKind.OneToMany,
                ReferencedEntity = one,
                ReferencingEntity = many,
                LookupAttribute = lookup
            };

        private static ErdModel Model(params EntityModel[] entities)
        {
            var m = new ErdModel
            {
                Solution = new SolutionInfo { FriendlyName = "Test", UniqueName = "test", Version = "1.0" }
            };
            m.Entities.AddRange(entities);
            return m;
        }

        // -------------------------------------------------------------- tests

        [Fact]
        public void Builds_nodes_and_one_to_many_edge()
        {
            var model = Model(Entity("account"), Entity("contact"));
            model.Relationships.Add(OneToMany("contact_account", "account", "contact", "parentcustomerid"));

            var graph = ErdGraphBuilder.BuildGraph(model, new ErdOptions());

            Assert.Equal(2, graph.Nodes.Count);
            var edge = Assert.Single(graph.Edges);
            Assert.Equal("account", edge.FromId);
            Assert.Equal("contact", edge.ToId);
            Assert.Equal(RelationshipKind.OneToMany, edge.Kind);
        }

        [Fact]
        public void Deduplicates_mirrored_relationship_registrations()
        {
            var model = Model(Entity("account"), Entity("contact"));
            // The same schema name arrives once from each entity's collection.
            model.Relationships.Add(OneToMany("contact_account", "account", "contact", "parentcustomerid"));
            model.Relationships.Add(OneToMany("contact_account", "account", "contact", "parentcustomerid"));

            var graph = ErdGraphBuilder.BuildGraph(model, new ErdOptions());

            Assert.Single(graph.Edges);
        }

        [Fact]
        public void System_relationships_hidden_by_default_shown_on_request()
        {
            var model = Model(Entity("account"));
            model.Relationships.Add(OneToMany("user_accounts", "systemuser", "account", "ownerid"));

            var hidden = ErdGraphBuilder.BuildGraph(model, new ErdOptions());
            Assert.Empty(hidden.Edges);

            var shown = ErdGraphBuilder.BuildGraph(model,
                new ErdOptions { IncludeSystemRelationships = true });
            Assert.Single(shown.Edges);
            Assert.Contains(shown.Nodes, n => n.Id == "systemuser"); // external stub
        }

        [Fact]
        public void External_reference_creates_stub_node_unless_disabled()
        {
            var model = Model(Entity("myentity", custom: true));
            model.Relationships.Add(OneToMany("myentity_account", "account", "myentity", "accountid"));

            var withStubs = ErdGraphBuilder.BuildGraph(model, new ErdOptions());
            Assert.Equal(2, withStubs.Nodes.Count);
            Assert.Equal(NodeFlavor.External, withStubs.Nodes.First(n => n.Id == "account").Flavor);

            var without = ErdGraphBuilder.BuildGraph(model,
                new ErdOptions { IncludeExternalEntities = false });
            Assert.Single(without.Nodes);
            Assert.Empty(without.Edges);
        }

        [Fact]
        public void Intersect_entities_never_become_boxes()
        {
            var model = Model(Entity("account"), Entity("lead"),
                Entity("accountleads", intersect: true));
            model.Relationships.Add(new RelationshipModel
            {
                SchemaName = "accountleads_association",
                Kind = RelationshipKind.ManyToMany,
                ReferencedEntity = "account",
                ReferencingEntity = "lead",
                IntersectEntity = "accountleads"
            });

            var graph = ErdGraphBuilder.BuildGraph(model, new ErdOptions());

            Assert.Equal(2, graph.Nodes.Count);
            var edge = Assert.Single(graph.Edges);
            Assert.Equal(RelationshipKind.ManyToMany, edge.Kind);
            Assert.Equal("accountleads", edge.Label);
        }

        [Fact]
        public void Many_to_many_can_be_toggled_off()
        {
            var model = Model(Entity("account"), Entity("lead"));
            model.Relationships.Add(new RelationshipModel
            {
                SchemaName = "accountleads_association",
                Kind = RelationshipKind.ManyToMany,
                ReferencedEntity = "account",
                ReferencingEntity = "lead",
                IntersectEntity = "accountleads"
            });

            var graph = ErdGraphBuilder.BuildGraph(model,
                new ErdOptions { IncludeManyToMany = false });
            Assert.Empty(graph.Edges);
        }

        [Fact]
        public void Parallel_relationships_are_fanned()
        {
            var model = Model(Entity("account"), Entity("contact"));
            model.Relationships.Add(OneToMany("r1", "account", "contact", "parentcustomerid"));
            model.Relationships.Add(OneToMany("r2", "account", "contact", "originatingaccountid"));
            model.Relationships.Add(OneToMany("r3", "contact", "account", "primarycontactid"));

            var graph = ErdGraphBuilder.BuildGraph(model, new ErdOptions());

            Assert.Equal(3, graph.Edges.Count);
            Assert.All(graph.Edges, e => Assert.Equal(3, e.ParallelCount));
            Assert.Equal(new[] { 0, 1, 2 },
                graph.Edges.Select(e => e.ParallelIndex).OrderBy(i => i).ToArray());
        }

        [Fact]
        public void Self_referential_marked_and_toggleable()
        {
            var model = Model(Entity("account"));
            model.Relationships.Add(OneToMany("account_parent_account", "account", "account", "parentaccountid"));

            var graph = ErdGraphBuilder.BuildGraph(model, new ErdOptions());
            var edge = Assert.Single(graph.Edges);
            Assert.True(edge.IsSelf);

            var off = ErdGraphBuilder.BuildGraph(model,
                new ErdOptions { IncludeSelfReferential = false });
            Assert.Empty(off.Edges);
        }

        [Fact]
        public void Selected_entities_filter_boxes_and_edges()
        {
            var model = Model(Entity("account"), Entity("contact"), Entity("lead"));
            model.Relationships.Add(OneToMany("r1", "account", "contact", "parentcustomerid"));
            model.Relationships.Add(OneToMany("r2", "lead", "contact", "originatingleadid"));

            var graph = ErdGraphBuilder.BuildGraph(model, new ErdOptions
            {
                IncludeExternalEntities = false,
                SelectedEntities = new HashSet<string> { "account", "contact" }
            });

            Assert.Equal(2, graph.Nodes.Count);
            Assert.Single(graph.Edges);
        }

        [Fact]
        public void Attribute_modes_control_rows()
        {
            var entity = Entity("account");
            entity.Attributes.Add(Lookup("primarycontactid", "contact", custom: false));
            entity.Attributes.Add(new AttributeModel
            {
                LogicalName = "revenue",
                DisplayName = "Annual Revenue",
                TypeLabel = "Currency",
                IsCustom = false
            });
            var model = Model(entity);

            var keys = ErdGraphBuilder.BuildGraph(model,
                new ErdOptions { AttributeMode = AttributeDisplayMode.KeysAndLookups });
            Assert.Equal(3, keys.Nodes[0].Rows.Count); // PK, name, lookup

            var all = ErdGraphBuilder.BuildGraph(model,
                new ErdOptions { AttributeMode = AttributeDisplayMode.All });
            Assert.Equal(4, all.Nodes[0].Rows.Count);

            var none = ErdGraphBuilder.BuildGraph(model,
                new ErdOptions { AttributeMode = AttributeDisplayMode.None });
            Assert.Empty(none.Nodes[0].Rows);
        }

        [Fact]
        public void Attribute_cap_produces_more_count()
        {
            var entity = Entity("account");
            for (int i = 0; i < 50; i++)
                entity.Attributes.Add(new AttributeModel
                {
                    LogicalName = "field" + i,
                    DisplayName = "Field " + i,
                    TypeLabel = "Text"
                });
            var model = Model(entity);

            var graph = ErdGraphBuilder.BuildGraph(model, new ErdOptions
            {
                AttributeMode = AttributeDisplayMode.All,
                MaxAttributesPerEntity = 10
            });

            Assert.Equal(10, graph.Nodes[0].Rows.Count);
            Assert.Equal(42, graph.Nodes[0].MoreCount); // 50 + PK + name - 10
        }

        [Fact]
        public void Edge_labels_can_be_disabled()
        {
            var model = Model(Entity("account"), Entity("contact"));
            var rel = OneToMany("r1", "account", "contact", "parentcustomerid");
            rel.LookupDisplayName = "Parent Customer";
            model.Relationships.Add(rel);

            var on = ErdGraphBuilder.BuildGraph(model, new ErdOptions());
            Assert.Equal("Parent Customer", on.Edges[0].Label);

            var off = ErdGraphBuilder.BuildGraph(model, new ErdOptions { ShowEdgeLabels = false });
            Assert.Null(off.Edges[0].Label);
        }
    }
}
