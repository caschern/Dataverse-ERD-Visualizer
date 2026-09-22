using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DataverseErdVisualizer;
using DataverseErdVisualizer.Exporters;
using DataverseErdVisualizer.Models;
using DataverseErdVisualizer.Rendering;
using Xunit;

namespace DataverseErdVisualizer.Tests
{
    public class MarkdownExporterTests
    {
        private static ErdDiagram Build() => Diagram(BuildModel());

        private static ErdDiagram Diagram(ErdModel model)
        {
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            using (var measure = new GdiDiagramSurface(g))
                return ErdGraphBuilder.Build(model, new ErdOptions(), measure);
        }

        private static ErdModel BuildModel()
        {
            var model = new ErdModel
            {
                Solution = new SolutionInfo
                {
                    FriendlyName = "Case Management",
                    UniqueName = "casemgmt",
                    Version = "1.0.0.0"
                }
            };

            EntityModel Entity(string logical, string display, bool custom = false)
            {
                var e = new EntityModel
                {
                    LogicalName = logical,
                    SchemaName = logical,
                    DisplayName = display,
                    PrimaryIdAttribute = logical + "id",
                    PrimaryNameAttribute = "name",
                    OwnershipType = "UserOwned",
                    IsCustom = custom,
                    Description = display + " records."
                };
                e.Attributes.Add(new AttributeModel
                {
                    LogicalName = logical + "id",
                    DisplayName = display + " Id",
                    IsPrimaryId = true,
                    TypeLabel = "GUID",
                    RequiredLevel = "SystemRequired"
                });
                e.Attributes.Add(new AttributeModel
                {
                    LogicalName = "name",
                    DisplayName = "Name",
                    IsPrimaryName = true,
                    TypeLabel = "Text",
                    RequiredLevel = "ApplicationRequired"
                });
                model.Entities.Add(e);
                return e;
            }

            var contact = Entity("contact", "Contact");
            var kase = Entity("cc_case", "Case", custom: true);
            var lookup = new AttributeModel
            {
                LogicalName = "cc_judgeid",
                DisplayName = "Assigned Judge",
                IsLookup = true,
                TypeLabel = "Lookup(contact)",
                RequiredLevel = "None"
            };
            lookup.Targets.Add("contact");
            kase.Attributes.Add(lookup);

            var priority = new AttributeModel
            {
                LogicalName = "cc_priority",
                DisplayName = "Priority",
                TypeLabel = "Choice",
                RequiredLevel = "None",
                Description = "How quickly the case must be heard."
            };
            priority.Options.Add(new OptionModel { Value = 100000000, Label = "Low" });
            priority.Options.Add(new OptionModel { Value = 100000001, Label = "High" });
            kase.Attributes.Add(priority);

            var statusReason = new AttributeModel
            {
                LogicalName = "statuscode",
                DisplayName = "Status Reason",
                TypeLabel = "Status Reason"
            };
            statusReason.Options.Add(new OptionModel { Value = 1, Label = "In Progress", StateLabel = "Active" });
            statusReason.Options.Add(new OptionModel { Value = 5, Label = "Resolved", StateLabel = "Inactive" });
            kase.Attributes.Add(statusReason);

            model.Relationships.Add(new RelationshipModel
            {
                SchemaName = "cc_contact_case",
                Kind = RelationshipKind.OneToMany,
                ReferencedEntity = "contact",
                ReferencingEntity = "cc_case",
                LookupAttribute = "cc_judgeid",
                LookupDisplayName = "Assigned Judge",
                Cascade = new CascadeModel
                {
                    Delete = "RemoveLink",
                    Assign = "NoCascade",
                    Share = "NoCascade",
                    Unshare = "NoCascade",
                    Reparent = "NoCascade"
                }
            });

            return model;
        }

        [Fact]
        public void Gives_every_table_its_own_section()
        {
            var md = MarkdownExporter.Generate(Build());

            Assert.Contains("## Contact (`contact`)", md);
            Assert.Contains("## Case (`cc_case`)", md);
            Assert.Contains("### Columns of Contact", md);
            Assert.Contains("### Relationships of Contact", md);
        }

        [Fact]
        public void States_each_relationship_from_both_tables()
        {
            var md = MarkdownExporter.Generate(Build());

            // Asking "what references Contact?" must hit Contact's own section,
            // and asking "what does Case point at?" must hit Case's.
            Assert.Contains("**Contact** is referenced by **Case**", md);
            Assert.Contains("**Case** references **Contact**", md);
            Assert.Contains("Assigned Judge", md);
            Assert.Contains("cc_contact_case", md);
        }

        [Fact]
        public void Sections_name_their_table_rather_than_relying_on_the_heading()
        {
            var md = MarkdownExporter.Generate(Build());
            var caseSection = md.Substring(md.IndexOf("## Case (`cc_case`)", StringComparison.Ordinal));

            // A retrieved chunk arrives without its neighbours, so the body has
            // to repeat the subject instead of saying "it".
            Assert.Contains("**Case** is a custom table", caseSection);
        }

        [Fact]
        public void Documents_every_column_regardless_of_diagram_display_mode()
        {
            // The diagram is built with the default "keys and lookups" mode, but
            // the knowledge base must still carry the full column list.
            var md = MarkdownExporter.Generate(Build());

            Assert.Contains("`cc_judgeid`", md);
            Assert.Contains("Optional", md);
            Assert.Contains("System required", md);
        }

        [Fact]
        public void Columns_are_self_describing_bullets_not_a_table()
        {
            var md = MarkdownExporter.Generate(Build());

            // A chunk boundary inside a Markdown table strands rows from their
            // header; every bullet has to survive being split out on its own.
            Assert.DoesNotContain("| --- |", md);
            Assert.Contains("- **Assigned Judge** (`cc_judgeid`) — Lookup to `contact`. Optional.", md);
            Assert.Contains("Primary key of Case.", md);
        }

        [Fact]
        public void Per_table_export_writes_one_file_each_plus_an_overview()
        {
            var folder = Path.Combine(Path.GetTempPath(), "erd-kb-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var result = MarkdownExporter.SavePerTable(Build(), folder);

                // Written into a subfolder named for the solution, never loose
                // in the chosen folder beside unrelated documentation.
                Assert.Equal(Path.Combine(folder, "Case Management-knowledge-base"), result.FolderPath);
                Assert.Empty(Directory.GetFiles(folder, "*.md"));

                var files = Directory.GetFiles(result.FolderPath, "*.md").Select(Path.GetFileName).ToList();
                Assert.Equal(3, result.FileCount);          // 2 tables + overview
                Assert.Contains("00-model-overview.md", files);
                Assert.Contains("contact.md", files);
                Assert.Contains("cc_case.md", files);
                Assert.Empty(result.StaleFiles);

                var caseFile = File.ReadAllText(Path.Combine(result.FolderPath, "cc_case.md"));

                // Standalone file: top-level heading, and it must carry its own
                // provenance and identity because nothing else travels with it.
                Assert.StartsWith("# Case (`cc_case`)", caseFile);
                Assert.Contains("Case Management", caseFile);
                Assert.Contains("**Case** is a custom table", caseFile);
                Assert.Contains("## Columns of Case", caseFile);
                Assert.Contains("## Relationships of Case", caseFile);

                // The relationship appears in BOTH files, phrased from each side.
                var contactFile = File.ReadAllText(Path.Combine(result.FolderPath, "contact.md"));
                Assert.Contains("**Case** references **Contact**", caseFile);
                Assert.Contains("**Contact** is referenced by **Case**", contactFile);

                var overview = File.ReadAllText(Path.Combine(result.FolderPath, "00-model-overview.md"));
                Assert.Contains("Model overview", overview);
                Assert.Contains("own file in this folder", overview);
                Assert.Contains("Tables documented: 2", overview);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void Names_the_hubs_and_lists_the_scope()
        {
            var md = MarkdownExporter.Generate(Build());

            Assert.Contains("## Model overview", md);
            Assert.Contains("Tables documented: 2", md);
            Assert.Contains("All tables covered:", md);
        }

        [Fact]
        public void Leftovers_from_an_earlier_export_are_reported()
        {
            var folder = Path.Combine(Path.GetTempPath(), "erd-kb-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var first = MarkdownExporter.SavePerTable(Build(), folder);

                // A table that existed last time but not now: its file lingers
                // and would teach the agent a model that is out of date.
                File.WriteAllText(Path.Combine(first.FolderPath, "cc_deletedtable.md"), "# Gone");

                var second = MarkdownExporter.SavePerTable(Build(), folder);

                Assert.Equal(first.FolderPath, second.FolderPath);   // same run, same place
                Assert.Equal(new[] { "cc_deletedtable.md" }, second.StaleFiles.ToArray());
                Assert.Equal(3, second.FileCount);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void Self_referential_relationships_state_their_cascade_behaviour()
        {
            var model = BuildModel();
            model.Entities.Single(e => e.LogicalName == "cc_case").Attributes.Add(new AttributeModel
            {
                LogicalName = "cc_parentcaseid",
                DisplayName = "Parent Case",
                IsLookup = true,
                TypeLabel = "Lookup(cc_case)"
            });
            model.Relationships.Add(new RelationshipModel
            {
                SchemaName = "cc_case_parent_case",
                Kind = RelationshipKind.OneToMany,
                ReferencedEntity = "cc_case",
                ReferencingEntity = "cc_case",
                LookupAttribute = "cc_parentcaseid",
                LookupDisplayName = "Parent Case",
                Cascade = new CascadeModel
                {
                    Delete = "Cascade", Assign = "Cascade", Share = "Cascade",
                    Unshare = "Cascade", Reparent = "Cascade"
                }
            });

            var md = MarkdownExporter.Generate(Diagram(model));

            // A hierarchy is exactly where "what happens to the children when I
            // delete the parent?" gets asked.
            Assert.Contains("forming a hierarchy of Case records", md);
            Assert.Contains("Behaviour: Parental.", md);
            Assert.Contains("Deleting Case records also deletes their related child Case records.", md);
        }

        [Fact]
        public void A_table_can_have_a_many_to_many_with_itself()
        {
            // Linking records to other records of the same table is not a
            // parent-child hierarchy: there is no lookup column to name and no
            // cascade behaviour to describe.
            var model = BuildModel();
            model.Relationships.Add(new RelationshipModel
            {
                SchemaName = "cc_case_cc_case",
                Kind = RelationshipKind.ManyToMany,
                ReferencedEntity = "cc_case",
                ReferencingEntity = "cc_case",
                IntersectEntity = "cc_case_cc_case_intersect"
            });

            var md = MarkdownExporter.Generate(Diagram(model));

            Assert.Contains(
                "**Case** has a many-to-many relationship with itself: Case records can be linked " +
                "to other Case records, through the intersect table `cc_case_cc_case_intersect`.", md);

            // It must not be described as a hierarchy, and must never render an
            // empty column name from the lookup fields an N:N does not have.
            Assert.DoesNotContain("**Case** references itself", md);
            Assert.DoesNotContain("****", md);
        }

        [Fact]
        public void Choice_columns_list_their_values_with_the_stored_numbers()
        {
            var md = MarkdownExporter.Generate(Build());

            // Flows, FetchXML and the Web API address choices by number, so an
            // agent given only the labels cannot write a working query.
            Assert.Contains("Allowed values: Low = 100000000; High = 100000001.", md);
        }

        [Fact]
        public void Status_reasons_say_which_status_they_belong_to()
        {
            var md = MarkdownExporter.Generate(Build());

            Assert.Contains("In Progress = 1 (status Active)", md);
            Assert.Contains("Resolved = 5 (status Inactive)", md);
        }

        [Fact]
        public void Column_descriptions_are_included()
        {
            var md = MarkdownExporter.Generate(Build());

            Assert.Contains("How quickly the case must be heard.", md);
        }

        [Fact]
        public void Cascade_behaviour_is_stated_from_both_tables()
        {
            var md = MarkdownExporter.Generate(Build());

            Assert.Contains("Behaviour: Referential.", md);

            // Both sides describe the same fact, and both must name the parent
            // and child the right way round.
            var deleteSentence =
                "Deleting Contact records clears the Assigned Judge lookup on related Case records, which are kept.";
            Assert.Equal(2, Regex.Matches(md, Regex.Escape(deleteSentence)).Count);
            Assert.DoesNotContain("Deleting Case records clears", md);
        }

        [Fact]
        public void Embeds_no_diagram_geometry()
        {
            var md = MarkdownExporter.Generate(Build());

            // Image data would swamp every retrieval chunk with coordinates.
            Assert.DoesNotContain("<svg", md);
            Assert.DoesNotContain("<path", md);
            Assert.DoesNotContain("base64", md);
        }
    }
}
