using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
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

            // Satellite children of Contact: their only relationship is the
            // lookup back to Contact, so they should pack into a grid below it.
            foreach (var name in new[] { "Preference", "Consent", "Alias", "Interest",
                                         "Skill", "Award", "Referral Source", "Portal Login" })
            {
                var logical = "cc_" + name.Replace(" ", "").ToLowerInvariant();
                var satellite = Entity(logical, name, custom: true);
                AddLookup(satellite, "cc_contactid", "Contact", "contact");
                Rel("cc_contact_" + logical, "contact", logical, "cc_contactid", "Contact");
            }

            // Satellites carrying SEVERAL lookups to the same hub: still
            // satellites (one distinct neighbour), shown as one "xN" connector.
            foreach (var name in new[] { "Case Note", "Case Task", "Case Event",
                                         "Case Filing", "Case Motion" })
            {
                var logical = "cc_" + name.Replace(" ", "").ToLowerInvariant();
                var satellite = Entity(logical, name, custom: true);
                AddLookup(satellite, "cc_authorid", "Author", "contact");
                AddLookup(satellite, "cc_reviewerid", "Reviewer", "contact");
                AddLookup(satellite, "cc_filedbyid", "Filed By", "contact");
                Rel("cc_author_" + logical, "contact", logical, "cc_authorid", "Author");
                Rel("cc_reviewer_" + logical, "contact", logical, "cc_reviewerid", "Reviewer");
                Rel("cc_filedby_" + logical, "contact", logical, "cc_filedbyid", "Filed By");
            }

            // Satellite parents of Work Order: classic lookup/reference tables
            // referenced by nothing else, so they pack into a grid above it.
            foreach (var name in new[] { "Work Order Type", "Work Order Status", "Priority",
                                         "Service Territory", "Trade", "Incident Type" })
            {
                var logical = "cc_" + name.Replace(" ", "").ToLowerInvariant();
                Entity(logical, name, custom: true);
                AddLookup(workorder, logical + "id", name, logical);
                Rel("cc_" + logical + "_workorder", logical, "cc_workorder", logical + "id", name);
            }

            // A satellite related in BOTH directions: its bus routes get
            // reversed, which is the case that used to drop labels inside a box.
            var household = Entity("cc_household", "Household", custom: true);
            AddLookup(household, "cc_contactid", "Head of Household", "contact");
            AddLookup(contact, "cc_householdid", "Household", "cc_household");
            Rel("cc_contact_household", "contact", "cc_household", "cc_contactid", "Head of Household");
            Rel("cc_household_contact", "cc_household", "contact", "cc_householdid", "Household");

            // External stub target (pricelevel is referenced but not in the solution).
            model.Entities.Add(new EntityModel
            {
                LogicalName = "pricelevel",
                DisplayName = "Price List",
                IsExternal = true
            });

            return model;
        }

        /// <summary>
        /// Parses the generated SVG and fails if any rotated relationship label
        /// overlaps a table box. Reading the real output is the only way to
        /// catch label placement bugs — the geometry is decided by the renderer,
        /// not the layout, so no model-level assertion would see them.
        /// </summary>
        private static void AssertNoLabelSitsInsideABox(string svg)
        {
            var rects = Regex.Matches(svg,
                "<rect x=\"([-0-9.]+)\" y=\"([-0-9.]+)\" width=\"([-0-9.]+)\" height=\"([-0-9.]+)\" rx=\"([-0-9.]+)\"[^>]*?(fill=\"[^\"]*\")[^>]*>");

            var boxes = new List<RectangleF>();
            var labels = new List<RectangleF>();
            foreach (Match m in rects)
            {
                float F(int g) => float.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
                var r = new RectangleF(F(1), F(2), F(3), F(4));
                float rx = F(5);
                bool outlined = m.Value.Contains("stroke=");
                if (rx >= 5f && m.Groups[6].Value == "fill=\"none\"" && outlined) boxes.Add(r);
                else if (rx == 2f && m.Groups[6].Value == "fill=\"#FFFFFF\"") labels.Add(r);
            }

            Assert.NotEmpty(boxes);
            Assert.NotEmpty(labels);

            var offenders = new List<string>();
            foreach (var label in labels)
                foreach (var box in boxes)
                    if (label.IntersectsWith(box))
                    {
                        offenders.Add(
                            $"label({label.X:0},{label.Y:0}..{label.Bottom:0}) in box({box.Y:0}..{box.Bottom:0})");
                        break;
                    }

            Assert.True(offenders.Count == 0,
                $"{offenders.Count} of {labels.Count} labels overlap a table box: " +
                string.Join("; ", offenders.Take(5)));
        }

        /// <summary>
        /// Every relationship drawn separately — the mode that exposes the
        /// reversed bus routes of satellites related in both directions, whose
        /// labels once landed inside their own box.
        /// </summary>
        [Fact]
        public void Labels_stay_out_of_boxes_when_every_satellite_edge_is_drawn()
        {
            var model = SampleModel();
            ErdDiagram diagram;
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            using (var measure = new GdiDiagramSurface(g))
            {
                diagram = ErdGraphBuilder.Build(model,
                    new ErdOptions { ShowAllSatelliteRelationships = true }, measure);
            }

            // The both-directions satellite must really be in a grid, or this
            // test would pass without exercising anything.
            var household = diagram.Graph.Nodes.Single(n => n.Id == "cc_household");
            var edges = diagram.Graph.Edges.Where(e =>
                e.FromId == household.Id || e.ToId == household.Id).ToList();
            Assert.Equal(2, edges.Count);
            Assert.All(edges, e => Assert.False(e.Hidden));
            Assert.Contains(edges, e => e.LabelAtSource);
            Assert.Contains(edges, e => !e.LabelAtSource);

            AssertNoLabelSitsInsideABox(SvgExporter.Generate(diagram));
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

            AssertNoLabelSitsInsideABox(svg);

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
