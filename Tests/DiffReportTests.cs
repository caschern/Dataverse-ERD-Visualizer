using System;
using System.IO;
using System.Linq;
using DataverseErdVisualizer.Compare;
using DataverseErdVisualizer.Models;
using Xunit;

namespace DataverseErdVisualizer.Tests
{
    public class DiffReportTests
    {
        private static ModelSnapshot Snapshot(ErdModel model, string environment, int day) => new ModelSnapshot
        {
            Model = model,
            Environment = environment,
            CapturedOn = new DateTime(2026, 9, day, 12, 0, 0, DateTimeKind.Utc)
        };

        /// <summary>Prod as the baseline, Dev carrying one of every kind of change.</summary>
        private static (ModelSnapshot baseline, ModelSnapshot current) ProdAndDev()
        {
            var prod = SnapshotStoreTests.RichModel();
            var dev = SnapshotStoreTests.RichModel();
            dev.Solution.Version = "2026.9.15.1";

            dev.Entities.Add(new EntityModel { LogicalName = "jn_hearing", DisplayName = "Hearing", IsCustom = true });
            var kase = dev.Entities.Single(e => e.LogicalName == "jn_case");
            kase.DisplayName = "Court Case";
            kase.Attributes.Add(new AttributeModel { LogicalName = "jn_priority", DisplayName = "Priority", TypeLabel = "Choice" });
            var status = kase.Attributes.Single(a => a.LogicalName == "statuscode");
            status.Options.Add(new OptionModel { Value = 3, Label = "Sealed", StateLabel = "Inactive" });
            status.RequiredLevel = "ApplicationRequired";

            dev.Relationships.Single().Cascade.Delete = "Restrict";
            dev.Relationships.Add(new RelationshipModel
            {
                SchemaName = "jn_case_hearing", Kind = RelationshipKind.OneToMany,
                ReferencedEntity = "jn_case", ReferencingEntity = "jn_hearing", LookupAttribute = "jn_caseid"
            });
            dev.Relationships.Add(new RelationshipModel
            {
                SchemaName = "lk_jn_hearing_createdby", Kind = RelationshipKind.OneToMany,
                ReferencedEntity = "systemuser", ReferencingEntity = "jn_hearing", LookupAttribute = "createdby"
            });

            return (Snapshot(prod, "Prod", 1), Snapshot(dev, "Dev", 15));
        }

        private static string Report()
        {
            var (baseline, current) = ProdAndDev();
            return DiffReport.Generate(baseline, current, ModelDiff.Compare(baseline.Model, current.Model));
        }

        [Fact]
        public void Matching_models_say_so_plainly()
        {
            var a = Snapshot(SnapshotStoreTests.RichModel(), "Prod", 1);
            var b = Snapshot(SnapshotStoreTests.RichModel(), "Dev", 2);

            var md = DiffReport.Generate(a, b, ModelDiff.Compare(a.Model, b.Model));

            Assert.Contains("No differences: the two data models match.", md);
            Assert.DoesNotContain("## Tables", md);
            Assert.DoesNotContain("## Relationships", md);
        }

        [Fact]
        public void The_header_names_both_sides()
        {
            var md = Report();

            Assert.Contains("| Source | Prod | Dev |", md);
            Assert.Contains("casemgmt v2026.8.1.1 (managed)", md);
            Assert.Contains("casemgmt v2026.9.15.1 (managed)", md);
        }

        [Fact]
        public void The_summary_counts_each_kind_of_change()
        {
            var md = Report();

            Assert.Contains("- Tables: **1 added**, **1 changed**", md);
            Assert.Contains("- Relationships: **1 added**, **1 changed**", md);
            Assert.Contains("- System relationships: **1 added** (listed separately at the end)", md);
        }

        [Fact]
        public void Table_and_column_changes_are_spelled_out()
        {
            var md = Report();

            Assert.Contains("## Tables added", md);
            Assert.Contains("- **Hearing** (`jn_hearing`) — custom table, 0 columns", md);
            Assert.Contains("### Court Case (`jn_case`)", md);
            Assert.Contains("- Display name: \"Case\" → \"Court Case\"", md);
            Assert.Contains("- Column added: **Priority** (`jn_priority`) — Choice", md);
            Assert.Contains("- Column changed: **Status Reason** (`statuscode`)", md);
            Assert.Contains("  - Required: \"Optional\" → \"Required\"", md);
            Assert.Contains("  - Allowed value 3: added \"Sealed (status Inactive)\"", md);
        }

        [Fact]
        public void Cascade_changes_are_named_and_put_into_words()
        {
            var md = Report();

            Assert.Contains("  - Behaviour: Referential → Referential, restrict delete", md);
            Assert.Contains("  - Cascade on delete: \"remove link\" → \"restrict\"", md);
            Assert.DoesNotContain("RemoveLink", md);   // SDK names never reach the reader
        }

        [Fact]
        public void System_relationships_are_kept_out_of_the_main_list()
        {
            var md = Report();

            var main = md.Substring(0, md.IndexOf("## System relationship changes", StringComparison.Ordinal));
            Assert.DoesNotContain("lk_jn_hearing_createdby", main);
            Assert.Contains("`jn_case_hearing` — Court Case → Hearing (one-to-many, through `jn_caseid`)", main);

            var system = md.Substring(md.IndexOf("## System relationship changes", StringComparison.Ordinal));
            Assert.Contains("lk_jn_hearing_createdby", system);
        }

        [Fact]
        public void Comparing_two_different_solutions_is_called_out()
        {
            var (baseline, current) = ProdAndDev();
            current.Model.Solution.UniqueName = "othersolution";

            var md = DiffReport.Generate(baseline, current, ModelDiff.Compare(baseline.Model, current.Model));

            Assert.Contains("**These are two different solutions**", md);
        }

        [Fact]
        public void Writes_a_sample_for_review()
        {
            var dir = Environment.GetEnvironmentVariable("ERD_SMOKE_DIR");
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            var (baseline, current) = ProdAndDev();
            DiffReport.Save(baseline, current, ModelDiff.Compare(baseline.Model, current.Model),
                Path.Combine(dir, "sample-diff-report.md"));
        }
    }
}
