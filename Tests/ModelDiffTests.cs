using System.Linq;
using DataverseErdVisualizer.Compare;
using DataverseErdVisualizer.Models;
using Xunit;

namespace DataverseErdVisualizer.Tests
{
    public class ModelDiffTests
    {
        private static ErdModel Model() => SnapshotStoreTests.RichModel();

        private static EntityModel Case(ErdModel m) => m.Entities.Single(e => e.LogicalName == "jn_case");

        private static AttributeModel Column(ErdModel m, string logical)
            => Case(m).Attributes.Single(a => a.LogicalName == logical);

        [Fact]
        public void Identical_models_have_no_changes()
        {
            var diff = ModelDiff.Compare(Model(), Model());

            Assert.False(diff.HasChanges);
        }

        [Fact]
        public void An_added_and_a_removed_table_are_reported()
        {
            var before = Model();
            var after = Model();
            after.Entities.Add(new EntityModel { LogicalName = "jn_hearing", DisplayName = "Hearing", IsCustom = true });
            before.Entities.Add(new EntityModel { LogicalName = "jn_legacy", DisplayName = "Legacy" });

            var diff = ModelDiff.Compare(before, after);

            Assert.Contains(diff.Tables, t => t.Kind == ChangeKind.Added && t.LogicalName == "jn_hearing");
            Assert.Contains(diff.Tables, t => t.Kind == ChangeKind.Removed && t.LogicalName == "jn_legacy");
        }

        [Fact]
        public void Column_additions_removals_and_changes_are_reported()
        {
            var before = Model();
            var after = Model();
            Case(after).Attributes.Add(new AttributeModel
            {
                LogicalName = "jn_priority", DisplayName = "Priority", TypeLabel = "Choice"
            });
            Case(after).Attributes.RemoveAll(a => a.LogicalName == "jn_judgeid");
            Column(after, "statuscode").RequiredLevel = "ApplicationRequired";

            var table = Assert.Single(ModelDiff.Compare(before, after).Tables);

            Assert.Equal(ChangeKind.Changed, table.Kind);
            Assert.Contains(table.Columns, c => c.Kind == ChangeKind.Added && c.LogicalName == "jn_priority");
            Assert.Contains(table.Columns, c => c.Kind == ChangeKind.Removed && c.LogicalName == "jn_judgeid");

            var status = table.Columns.Single(c => c.LogicalName == "statuscode");
            var required = Assert.Single(status.Properties);
            Assert.Equal("Required", required.Property);
            Assert.Equal("Optional", required.Before);
            Assert.Equal("Required", required.After);
        }

        [Fact]
        public void A_lookup_target_change_is_reported_once_not_also_as_a_type_change()
        {
            var before = Model();
            var after = Model();
            var judge = Column(after, "jn_judgeid");
            judge.Targets.Clear();
            judge.Targets.Add("systemuser");
            judge.TypeLabel = "Lookup(systemuser)";

            var column = ModelDiff.Compare(before, after).Tables.Single().Columns.Single();

            var change = Assert.Single(column.Properties);
            Assert.Equal("Lookup targets", change.Property);
            Assert.Equal("contact", change.Before);
            Assert.Equal("systemuser", change.After);
        }

        [Fact]
        public void Choice_values_are_matched_by_number()
        {
            var before = Model();
            var after = Model();
            var status = Column(after, "statuscode");
            status.Options.Single(o => o.Value == 1).Label = "In Progress";           // relabel
            status.Options.RemoveAll(o => o.Value == 2);                                // removal
            status.Options.Add(new OptionModel { Value = 3, Label = "Sealed", StateLabel = "Inactive" }); // addition

            var changes = ModelDiff.Compare(before, after).Tables.Single().Columns.Single().Properties;

            var relabel = changes.Single(c => c.Property == "Allowed value 1");
            Assert.Equal("Open (status Active)", relabel.Before);
            Assert.Equal("In Progress (status Active)", relabel.After);

            var removed = changes.Single(c => c.Property == "Allowed value 2");
            Assert.Equal("Closed (status Inactive)", removed.Before);
            Assert.Null(removed.After);

            var added = changes.Single(c => c.Property == "Allowed value 3");
            Assert.Null(added.Before);
            Assert.Equal("Sealed (status Inactive)", added.After);
        }

        [Fact]
        public void A_status_reason_moving_to_another_status_is_a_change()
        {
            var before = Model();
            var after = Model();
            Column(after, "statuscode").Options.Single(o => o.Value == 2).StateLabel = "Active";

            var change = ModelDiff.Compare(before, after).Tables.Single().Columns.Single().Properties.Single();

            Assert.Equal("Closed (status Inactive)", change.Before);
            Assert.Equal("Closed (status Active)", change.After);
        }

        [Fact]
        public void A_description_that_was_never_set_is_not_a_change()
        {
            var before = Model();
            var after = Model();
            Column(before, "jn_judgeid").Description = null;
            Column(after, "jn_judgeid").Description = "   ";

            Assert.False(ModelDiff.Compare(before, after).HasChanges);
        }

        [Fact]
        public void Relationship_additions_removals_and_cascade_changes_are_reported()
        {
            var before = Model();
            var after = Model();
            after.Relationships.Add(new RelationshipModel
            {
                SchemaName = "jn_case_hearing", Kind = RelationshipKind.OneToMany,
                ReferencedEntity = "jn_case", ReferencingEntity = "jn_hearing", LookupAttribute = "jn_caseid"
            });
            before.Relationships.Add(new RelationshipModel
            {
                SchemaName = "jn_case_legacy", Kind = RelationshipKind.OneToMany,
                ReferencedEntity = "jn_case", ReferencingEntity = "jn_legacy", LookupAttribute = "jn_caseid"
            });
            after.Relationships.Single(r => r.SchemaName == "jn_contact_case").Cascade.Delete = "Restrict";

            var rels = ModelDiff.Compare(before, after).Relationships;

            Assert.Contains(rels, r => r.Kind == ChangeKind.Added && r.SchemaName == "jn_case_hearing");
            Assert.Contains(rels, r => r.Kind == ChangeKind.Removed && r.SchemaName == "jn_case_legacy");

            var changed = rels.Single(r => r.Kind == ChangeKind.Changed);
            var delete = Assert.Single(changed.Properties);
            Assert.Equal("Cascade on delete", delete.Property);
            Assert.Equal("RemoveLink", delete.Before);
            Assert.Equal("Restrict", delete.After);
            Assert.Equal("RemoveLink", changed.CascadeBefore.Delete);
            Assert.Equal("Restrict", changed.CascadeAfter.Delete);
        }

        [Fact]
        public void Unknown_cascade_on_one_side_is_not_reported_as_a_change()
        {
            var before = Model();
            var after = Model();
            before.Relationships.Single().Cascade = null;

            Assert.False(ModelDiff.Compare(before, after).HasChanges);
        }

        [Fact]
        public void A_relationship_registered_twice_is_compared_once()
        {
            // Each 1:N arrives once from each of its tables.
            var before = Model();
            var after = Model();
            var original = after.Relationships.Single();
            after.Relationships.Add(new RelationshipModel
            {
                SchemaName = original.SchemaName, Kind = original.Kind,
                ReferencedEntity = original.ReferencedEntity, ReferencingEntity = original.ReferencingEntity,
                LookupAttribute = original.LookupAttribute, Cascade = original.Cascade
            });

            Assert.False(ModelDiff.Compare(before, after).HasChanges);
        }

        [Fact]
        public void External_and_intersect_tables_are_not_compared_as_tables()
        {
            var before = Model();
            var after = Model();
            after.Entities.Add(new EntityModel { LogicalName = "pricelevel", IsExternal = true });
            after.Entities.Add(new EntityModel { LogicalName = "jn_case_contact", IsIntersect = true });

            Assert.Empty(ModelDiff.Compare(before, after).Tables);
        }

        [Fact]
        public void System_relationships_are_flagged_so_the_report_can_set_them_apart()
        {
            var before = Model();
            var after = Model();
            after.Relationships.Add(new RelationshipModel
            {
                SchemaName = "lk_jn_case_createdby", Kind = RelationshipKind.OneToMany,
                ReferencedEntity = "systemuser", ReferencingEntity = "jn_case", LookupAttribute = "createdby"
            });

            var rel = ModelDiff.Compare(before, after).Relationships.Single();

            Assert.True(rel.IsSystem);
        }

        [Fact]
        public void Relationship_ends_carry_display_names()
        {
            var before = Model();
            var after = Model();
            after.Entities.Add(new EntityModel { LogicalName = "contact", DisplayName = "Contact", IsExternal = true });
            after.Relationships.Add(new RelationshipModel
            {
                SchemaName = "jn_contact_case2", Kind = RelationshipKind.OneToMany,
                ReferencedEntity = "contact", ReferencingEntity = "jn_case", LookupAttribute = "jn_clerkid"
            });

            var rel = ModelDiff.Compare(before, after).Relationships.Single();

            Assert.Equal("Contact", rel.ReferencedDisplayName);
            Assert.Equal("Case", rel.ReferencingDisplayName);
        }
    }
}
