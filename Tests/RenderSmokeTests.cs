using System;
using System.Drawing;
using System.IO;
using DataverseErdVisualizer;
using DataverseErdVisualizer.Exporters;
using DataverseErdVisualizer.Models;
using DataverseErdVisualizer.Rendering;
using Xunit;

namespace DataverseErdVisualizer.Tests
{
    /// <summary>
    /// End-to-end render of a realistic sample model through the real GDI
    /// pipeline. Also writes PNG/SVG/HTML/Mermaid files for eyeballing when
    /// the ERD_SMOKE_DIR environment variable points at a folder.
    /// </summary>
    public class RenderSmokeTests
    {
        private static ErdModel SampleModel()
        {
            var model = new ErdModel
            {
                Solution = new SolutionInfo
                {
                    FriendlyName = "Field Service Sample",
                    UniqueName = "fieldservicesample",
                    Version = "1.2.0.0",
                    IsManaged = false
                }
            };

            EntityModel Entity(string logical, string display, bool custom = false, bool activity = false)
            {
                var e = new EntityModel
                {
                    LogicalName = logical,
                    SchemaName = logical,
                    DisplayName = display,
                    PrimaryIdAttribute = logical + "id",
                    PrimaryNameAttribute = "name",
                    IsCustom = custom,
                    IsActivity = activity,
                    OwnershipType = "UserOwned"
                };
                e.Attributes.Add(new AttributeModel
                {
                    LogicalName = logical + "id",
                    DisplayName = display + " Id",
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
                model.Entities.Add(e);
                return e;
            }

            void AddLookup(EntityModel e, string logical, string display, string target)
            {
                var a = new AttributeModel
                {
                    LogicalName = logical,
                    DisplayName = display,
                    IsLookup = true,
                    IsCustom = true,
                    TypeLabel = "Lookup(" + target + ")"
                };
                a.Targets.Add(target);
                e.Attributes.Add(a);
            }

            void Rel(string schema, string one, string many, string lookup, string lookupDisplay)
            {
                model.Relationships.Add(new RelationshipModel
                {
                    SchemaName = schema,
                    Kind = RelationshipKind.OneToMany,
                    ReferencedEntity = one,
                    ReferencingEntity = many,
                    LookupAttribute = lookup,
                    LookupDisplayName = lookupDisplay
                });
            }

            var account = Entity("account", "Account");
            account.Attributes.Add(new AttributeModel
            {
                LogicalName = "revenue",
                DisplayName = "Annual Revenue",
                TypeLabel = "Currency"
            });
            AddLookup(account, "parentaccountid", "Parent Account", "account");
            AddLookup(account, "primarycontactid", "Primary Contact", "contact");

            var contact = Entity("contact", "Contact");
            AddLookup(contact, "parentcustomerid", "Company Name", "account");

            var workorder = Entity("cc_workorder", "Work Order", custom: true);
            AddLookup(workorder, "cc_accountid", "Service Account", "account");
            AddLookup(workorder, "cc_contactid", "Reported By", "contact");
            AddLookup(workorder, "cc_pricelistid", "Price List", "pricelevel");

            var booking = Entity("cc_booking", "Resource Booking", custom: true);
            AddLookup(booking, "cc_workorderid", "Work Order", "cc_workorder");

            // Fan-in child: several labeled lookups from DIFFERENT parents —
            // the case that garbled labels before distinct entry ports existed.
            var district = Entity("cc_district", "District", custom: true);
            AddLookup(district, "cc_courthouseid", "Courthouse", "account");
            AddLookup(district, "cc_liaisonid", "Liaison", "contact");
            AddLookup(district, "cc_workorderid", "Origin Work Order", "cc_workorder");
            Rel("cc_account_district", "account", "cc_district", "cc_courthouseid", "Courthouse");
            Rel("cc_contact_district", "contact", "cc_district", "cc_liaisonid", "Liaison");
            Rel("cc_workorder_district", "cc_workorder", "cc_district", "cc_workorderid", "Origin Work Order");

            Rel("account_parent_account", "account", "account", "parentaccountid", "Parent Account");
            Rel("account_master_account", "account", "account", "masterid", "Master Record");
            Rel("contact_customer_accounts", "account", "contact", "parentcustomerid", "Company Name");
            Rel("account_primary_contact", "contact", "account", "primarycontactid", "Primary Contact");
            Rel("cc_account_workorder", "account", "cc_workorder", "cc_accountid", "Service Account");
            Rel("cc_contact_workorder", "contact", "cc_workorder", "cc_contactid", "Reported By");
            Rel("cc_pricelevel_workorder", "pricelevel", "cc_workorder", "cc_pricelistid", "Price List");
            Rel("cc_workorder_booking", "cc_workorder", "cc_booking", "cc_workorderid", "Work Order");

            model.Relationships.Add(new RelationshipModel
            {
                SchemaName = "cc_workorder_incidenttype",
                Kind = RelationshipKind.ManyToMany,
                ReferencedEntity = "cc_workorder",
                ReferencingEntity = "account",
                IntersectEntity = "cc_workorder_account"
            });

            // External stub target (pricelevel is referenced but not in the solution).
            model.Entities.Add(new EntityModel
            {
                LogicalName = "pricelevel",
                DisplayName = "Price List",
                IsExternal = true
            });

            return model;
        }

        [Fact]
        public void Renders_sample_model_through_all_exporters()
        {
            var model = SampleModel();
            ErdDiagram diagram;
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            using (var measure = new GdiDiagramSurface(g))
            {
                diagram = ErdGraphBuilder.Build(model, new ErdOptions(), measure);
            }

            Assert.True(diagram.Graph.Nodes.Count >= 5);
            Assert.True(diagram.CanvasSize.Width > 100 && diagram.CanvasSize.Height > 100);

            using (var png = PngExporter.RenderToBitmap(diagram, 2f))
                Assert.True(png.Width > 200 && png.Height > 200);

            var svg = SvgExporter.Generate(diagram);
            Assert.Contains("<svg", svg);
            Assert.Contains("Account", svg);

            var html = HtmlExporter.Generate(diagram);
            Assert.Contains("Work Order", html);

            var mermaid = MermaidExporter.Generate(diagram);
            Assert.Contains("erDiagram", mermaid);
            Assert.Contains("||--o{", mermaid);

            // Optional artifacts for human eyeballing.
            var dir = Environment.GetEnvironmentVariable("ERD_SMOKE_DIR");
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                PngExporter.Save(diagram, Path.Combine(dir, "smoke.png"));
                SvgExporter.Save(diagram, Path.Combine(dir, "smoke.svg"));
                HtmlExporter.Save(diagram, Path.Combine(dir, "smoke.html"));
                MermaidExporter.Save(diagram, Path.Combine(dir, "smoke.mmd"));
            }
        }
    }
}
