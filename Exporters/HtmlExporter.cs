using System;
using System.IO;
using System.Linq;
using System.Text;
using DataverseErdVisualizer.Models;

namespace DataverseErdVisualizer.Exporters
{
    /// <summary>
    /// Writes a self-contained HTML documentation page: the diagram as inline
    /// SVG (pannable via the browser scrollbars) followed by a data dictionary
    /// of every table, its columns and relationships.
    /// </summary>
    public static class HtmlExporter
    {
        public static void Save(ErdDiagram diagram, string path)
            => File.WriteAllText(path, Generate(diagram), Encoding.UTF8);

        public static string Generate(ErdDiagram diagram)
        {
            var graph = diagram.Graph;
            var sb = new StringBuilder();

            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
            sb.Append("<title>").Append(H(graph.Title ?? "Entity Relationship Diagram")).AppendLine("</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body{font-family:'Segoe UI',Arial,sans-serif;margin:0;color:#212936;background:#f5f6f8}");
            sb.AppendLine(".wrap{max-width:1200px;margin:0 auto;padding:24px}");
            sb.AppendLine("h1{font-size:22px;margin:0 0 4px}h2{font-size:16px;margin:28px 0 8px}");
            sb.AppendLine(".sub{color:#6e7681;font-size:13px;margin-bottom:16px}");
            sb.AppendLine(".diagram{background:#fff;border:1px solid #d8dce2;border-radius:8px;overflow:auto;max-height:75vh}");
            sb.AppendLine("table{border-collapse:collapse;width:100%;background:#fff;border:1px solid #d8dce2;border-radius:8px;font-size:13px}");
            sb.AppendLine("th,td{text-align:left;padding:6px 10px;border-bottom:1px solid #e8ebef;vertical-align:top}");
            sb.AppendLine("th{background:#eef1f5;font-weight:600}");
            sb.AppendLine(".badge{display:inline-block;font-size:10px;font-weight:700;color:#fff;border-radius:3px;padding:1px 5px;margin-right:6px}");
            sb.AppendLine(".pk{background:#f59f00}.nm{background:#388e3c}.fk{background:#1976d2}");
            sb.AppendLine(".muted{color:#828a94}");
            sb.AppendLine("</style></head><body><div class=\"wrap\">");

            sb.Append("<h1>").Append(H(graph.Title ?? "Entity Relationship Diagram")).AppendLine("</h1>");
            if (!string.IsNullOrEmpty(graph.Subtitle))
                sb.Append("<div class=\"sub\">").Append(H(graph.Subtitle))
                  .Append(" · generated ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
                  .AppendLine("</div>");

            sb.AppendLine("<div class=\"diagram\">");
            sb.AppendLine(SvgExporter.Generate(diagram)
                .Replace("<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Environment.NewLine, ""));
            sb.AppendLine("</div>");

            // ---- data dictionary ----
            foreach (var node in graph.Nodes.Where(n => n.Entity != null && !n.Entity.IsExternal)
                                            .OrderBy(n => n.Title, StringComparer.OrdinalIgnoreCase))
            {
                var entity = node.Entity;
                sb.Append("<h2 id=\"").Append(H(entity.LogicalName)).Append("\">")
                  .Append(H(entity.DisplayName ?? entity.LogicalName))
                  .Append(" <span class=\"muted\">(").Append(H(entity.LogicalName)).Append(")</span></h2>");
                if (!string.IsNullOrEmpty(entity.Description))
                    sb.Append("<div class=\"sub\">").Append(H(entity.Description)).AppendLine("</div>");

                sb.AppendLine("<table><tr><th style=\"width:34%\">Column</th><th style=\"width:22%\">Logical name</th><th style=\"width:20%\">Type</th><th>Detail</th></tr>");
                foreach (var attr in entity.Attributes
                    .OrderBy(a => a.IsPrimaryId ? 0 : a.IsPrimaryName ? 1 : a.IsLookup ? 2 : 3)
                    .ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase))
                {
                    string badge = attr.IsPrimaryId ? "<span class=\"badge pk\">PK</span>"
                                 : attr.IsPrimaryName ? "<span class=\"badge nm\">NAME</span>"
                                 : attr.IsLookup ? "<span class=\"badge fk\">FK</span>" : "";
                    string detail = attr.Targets.Count > 0 ? "→ " + H(string.Join(", ", attr.Targets)) : "";
                    sb.Append("<tr><td>").Append(badge).Append(H(attr.DisplayName ?? attr.LogicalName))
                      .Append("</td><td class=\"muted\">").Append(H(attr.LogicalName))
                      .Append("</td><td>").Append(H(attr.TypeLabel))
                      .Append("</td><td class=\"muted\">").Append(detail)
                      .AppendLine("</td></tr>");
                }
                sb.AppendLine("</table>");

                var rels = graph.Edges.Where(e =>
                        string.Equals(e.FromId, node.Id, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(e.ToId, node.Id, StringComparison.OrdinalIgnoreCase))
                    .Select(e => e.Relationship).Where(r => r != null).ToList();
                if (rels.Count > 0)
                {
                    sb.AppendLine("<table style=\"margin-top:8px\"><tr><th style=\"width:34%\">Relationship</th><th style=\"width:12%\">Type</th><th>Detail</th></tr>");
                    foreach (var rel in rels)
                    {
                        string type = rel.Kind == RelationshipKind.ManyToMany ? "N:N"
                            : string.Equals(rel.ReferencedEntity, entity.LogicalName, StringComparison.OrdinalIgnoreCase) ? "1:N" : "N:1";
                        string detail = rel.Kind == RelationshipKind.ManyToMany
                            ? H(rel.ReferencedEntity + " ↔ " + rel.ReferencingEntity + " via " + rel.IntersectEntity)
                            : H(rel.ReferencingEntity + "." + (rel.LookupDisplayName ?? rel.LookupAttribute) + " → " + rel.ReferencedEntity);
                        sb.Append("<tr><td>").Append(H(rel.SchemaName))
                          .Append("</td><td>").Append(type)
                          .Append("</td><td class=\"muted\">").Append(detail)
                          .AppendLine("</td></tr>");
                    }
                    sb.AppendLine("</table>");
                }
            }

            sb.AppendLine("<div class=\"sub\" style=\"margin-top:24px\">Generated by Dataverse ERD Visualizer for XrmToolBox</div>");
            sb.AppendLine("</div></body></html>");
            return sb.ToString();
        }

        private static string H(string s)
        {
            return (s ?? "")
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }
    }
}
