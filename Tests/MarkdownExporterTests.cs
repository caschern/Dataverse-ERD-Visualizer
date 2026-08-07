using System;
using System.Drawing;
using System.IO;
using System.Linq;
using DataverseErdVisualizer;
using DataverseErdVisualizer.Exporters;
using DataverseErdVisualizer.Models;
using DataverseErdVisualizer.Rendering;
using Xunit;

namespace DataverseErdVisualizer.Tests
{
    public class MarkdownExporterTests
    {
        private static ErdDiagram Build()
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

            model.Relationships.Add(new RelationshipModel
            {
                SchemaName = "cc_contact_case",
                Kind = RelationshipKind.OneToMany,
                ReferencedEntity = "contact",
                ReferencingEntity = "cc_case",
                LookupAttribute = "cc_judgeid",
                LookupDisplayName = "Assigned Judge"
            });

            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            using (var measure = new GdiDiagramSurface(g))
                return ErdGraphBuilder.Build(model, new ErdOptions(), measure);
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

                var files = Directory.GetFiles(folder, "*.md").Select(Path.GetFileName).ToList();
                Assert.Equal(3, result.FileCount);          // 2 tables + overview
                Assert.Contains("00-model-overview.md", files);
                Assert.Contains("contact.md", files);
                Assert.Contains("cc_case.md", files);

                var caseFile = File.ReadAllText(Path.Combine(folder, "cc_case.md"));

                // Standalone file: top-level heading, and it must carry its own
                // provenance and identity because nothing else travels with it.
                Assert.StartsWith("# Case (`cc_case`)", caseFile);
                Assert.Contains("Case Management", caseFile);
                Assert.Contains("**Case** is a custom table", caseFile);
                Assert.Contains("## Columns of Case", caseFile);
                Assert.Contains("## Relationships of Case", caseFile);

                // The relationship appears in BOTH files, phrased from each side.
                var contactFile = File.ReadAllText(Path.Combine(folder, "contact.md"));
                Assert.Contains("**Case** references **Contact**", caseFile);
                Assert.Contains("**Contact** is referenced by **Case**", contactFile);

                var overview = File.ReadAllText(Path.Combine(folder, "00-model-overview.md"));
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
