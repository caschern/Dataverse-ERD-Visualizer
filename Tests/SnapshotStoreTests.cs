using System;
using System.IO;
using System.Linq;
using DataverseErdVisualizer.Compare;
using DataverseErdVisualizer.Models;
using Xunit;

namespace DataverseErdVisualizer.Tests
{
    public class SnapshotStoreTests : IDisposable
    {
        private readonly string _path =
            Path.Combine(Path.GetTempPath(), "erd-" + Guid.NewGuid().ToString("N") + SnapshotStore.Extension);

        public void Dispose()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }

        internal static ErdModel RichModel()
        {
            var model = new ErdModel
            {
                Solution = new SolutionInfo
                {
                    Id = Guid.NewGuid(), UniqueName = "casemgmt", FriendlyName = "Case Management",
                    Version = "2026.8.1.1", Publisher = "Courts", IsManaged = true
                }
            };

            var kase = new EntityModel
            {
                LogicalName = "jn_case", SchemaName = "jn_Case", DisplayName = "Case",
                Description = "A matter before the court.", PrimaryIdAttribute = "jn_caseid",
                PrimaryNameAttribute = "jn_name", OwnershipType = "UserOwned", IsCustom = true
            };
            var status = new AttributeModel
            {
                LogicalName = "statuscode", DisplayName = "Status Reason", TypeLabel = "Status Reason",
                RequiredLevel = "None", Description = "Why the case is in its status."
            };
            status.Options.Add(new OptionModel { Value = 1, Label = "Open", StateLabel = "Active" });
            status.Options.Add(new OptionModel { Value = 2, Label = "Closed", StateLabel = "Inactive" });
            kase.Attributes.Add(status);

            var judge = new AttributeModel
            {
                LogicalName = "jn_judgeid", DisplayName = "Judge", TypeLabel = "Lookup(contact)",
                IsLookup = true, RequiredLevel = "ApplicationRequired"
            };
            judge.Targets.Add("contact");
            kase.Attributes.Add(judge);
            model.Entities.Add(kase);

            model.Relationships.Add(new RelationshipModel
            {
                SchemaName = "jn_contact_case", Kind = RelationshipKind.OneToMany,
                ReferencedEntity = "contact", ReferencingEntity = "jn_case",
                LookupAttribute = "jn_judgeid", LookupDisplayName = "Judge",
                Cascade = new CascadeModel
                {
                    Delete = "RemoveLink", Assign = "NoCascade", Share = "NoCascade",
                    Unshare = "NoCascade", Reparent = "NoCascade"
                }
            });
            return model;
        }

        [Fact]
        public void A_snapshot_round_trips_every_part_of_the_model()
        {
            var captured = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
            SnapshotStore.Save(new ModelSnapshot
            {
                CapturedOn = captured,
                Environment = "Prod",
                ToolVersion = "1.9.0",
                Model = RichModel()
            }, _path);

            var loaded = SnapshotStore.Load(_path);

            Assert.Equal("Prod", loaded.Environment);
            Assert.Equal(captured, loaded.CapturedOn.ToUniversalTime());
            Assert.Equal("casemgmt", loaded.Model.Solution.UniqueName);
            Assert.True(loaded.Model.Solution.IsManaged);

            // The read-only collections are the part a serializer can quietly drop.
            var kase = Assert.Single(loaded.Model.Entities);
            Assert.Equal("A matter before the court.", kase.Description);
            Assert.Equal(2, kase.Attributes.Count);

            var status = kase.Attributes.Single(a => a.LogicalName == "statuscode");
            Assert.Equal(2, status.Options.Count);
            Assert.Equal("Closed", status.Options[1].Label);
            Assert.Equal("Inactive", status.Options[1].StateLabel);
            Assert.Equal("Why the case is in its status.", status.Description);

            var judge = kase.Attributes.Single(a => a.LogicalName == "jn_judgeid");
            Assert.Equal(new[] { "contact" }, judge.Targets.ToArray());
            Assert.True(judge.IsLookup);

            var rel = Assert.Single(loaded.Model.Relationships);
            Assert.Equal(RelationshipKind.OneToMany, rel.Kind);
            Assert.Equal("RemoveLink", rel.Cascade.Delete);
        }

        [Fact]
        public void A_file_that_is_not_a_snapshot_is_reported_not_treated_as_empty()
        {
            File.WriteAllText(_path, "this is not xml at all");

            // Comparing against an empty model would claim every table was added.
            var ex = Assert.Throws<InvalidDataException>(() => SnapshotStore.Load(_path));
            Assert.Contains("not a readable ERD model snapshot", ex.Message);
        }

        [Fact]
        public void A_snapshot_from_a_newer_version_is_refused_with_a_clear_message()
        {
            SnapshotStore.Save(new ModelSnapshot { Model = RichModel(), Format = ModelSnapshot.CurrentFormat + 1 }, _path);

            var ex = Assert.Throws<InvalidDataException>(() => SnapshotStore.Load(_path));
            Assert.Contains("newer version", ex.Message);
        }

        [Fact]
        public void A_snapshot_without_a_model_is_refused()
        {
            SnapshotStore.Save(new ModelSnapshot { Environment = "Prod" }, _path);

            Assert.Throws<InvalidDataException>(() => SnapshotStore.Load(_path));
        }

        [Fact]
        public void Describe_names_the_source_version_and_time()
        {
            var snapshot = new ModelSnapshot
            {
                Environment = "Prod",
                CapturedOn = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
                Model = RichModel()
            };

            var text = snapshot.Describe();

            Assert.StartsWith("Prod · v2026.8.1.1 · 2026-09-01", text);
        }
    }
}
