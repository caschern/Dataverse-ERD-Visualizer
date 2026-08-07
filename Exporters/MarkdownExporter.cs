using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DataverseErdVisualizer.Models;

namespace DataverseErdVisualizer.Exporters
{
    /// <summary>Outcome of a per-table knowledge base export.</summary>
    public class MarkdownExportResult
    {
        public int FileCount { get; set; }
        public string OverviewPath { get; set; }
    }

    /// <summary>
    /// Writes the model as a Markdown knowledge base for grounding an AI agent
    /// (Copilot Studio and similar), rather than as a document to read.
    ///
    /// Retrieval, not layout, drives every choice here:
    ///   • one section per table, so chunkers split on table boundaries;
    ///   • each section names its table in full instead of saying "it", because
    ///     a chunk is retrieved without the sections around it;
    ///   • relationships are written as sentences from BOTH sides, so a question
    ///     about either table matches — a lookup listed only on the child would
    ///     never surface when asking what references the parent;
    ///   • columns are bullets, not a table: a chunk boundary landing inside a
    ///     Markdown table strands rows from their header, leaving the model to
    ///     guess which cell was the type and which the target. A bullet stays
    ///     interpretable however it is split;
    ///   • no diagram is embedded: image geometry would swamp every chunk.
    ///
    /// Exporting one file per table costs more files but reads back with more
    /// confidence: citations name the table, and a chunk can never straddle two
    /// tables, which is a common source of confident cross-table errors.
    /// </summary>
    public static class MarkdownExporter
    {
        // ------------------------------------------------------- single file

        public static void Save(ErdDiagram diagram, string path)
            => File.WriteAllText(path, Generate(diagram), new UTF8Encoding(false));

        public static string Generate(ErdDiagram diagram)
        {
            var graph = diagram.Graph;
            var tables = Tables(graph);

            var sb = new StringBuilder();
            WriteDocumentHeader(sb, graph, tables.Count);
            WriteOverview(sb, graph, tables, perTableFiles: false);

            foreach (var node in tables)
            {
                WriteTable(sb, graph, node, headingLevel: 2);
                sb.AppendLine("---");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        // ----------------------------------------------------- file per table

        /// <summary>
        /// Writes one Markdown file per table plus an overview file, into the
        /// given folder. Existing files with the same names are replaced.
        /// </summary>
        public static MarkdownExportResult SavePerTable(ErdDiagram diagram, string folder)
        {
            var graph = diagram.Graph;
            var tables = Tables(graph);
            var encoding = new UTF8Encoding(false);

            var overview = new StringBuilder();
            WriteDocumentHeader(overview, graph, tables.Count);
            WriteOverview(overview, graph, tables, perTableFiles: true);
            var overviewPath = Path.Combine(folder, "00-model-overview.md");
            File.WriteAllText(overviewPath, overview.ToString(), encoding);

            int count = 1;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "00-model-overview" };
            foreach (var node in tables)
            {
                var sb = new StringBuilder();
                // Every file repeats its provenance: a chunk retrieved from one
                // of these arrives with no sight of the overview.
                WriteTable(sb, graph, node, headingLevel: 1,
                    solutionName: graph.Title, solutionDetail: graph.Subtitle);
                File.WriteAllText(Path.Combine(folder, FileName(node, used) + ".md"),
                    sb.ToString(), encoding);
                count++;
            }

            return new MarkdownExportResult { FileCount = count, OverviewPath = overviewPath };
        }

        private static string FileName(ErdNode node, HashSet<string> used)
        {
            var name = node.Id;
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (name.Length > 80) name = name.Substring(0, 80);

            var candidate = name;
            int suffix = 2;
            while (!used.Add(candidate)) candidate = name + "-" + suffix++;
            return candidate;
        }

        // ------------------------------------------------------------ pieces

        private static List<ErdNode> Tables(ErdGraph graph)
            => graph.Nodes
                .Where(n => n.Entity != null && !n.Entity.IsExternal)
                .OrderBy(n => n.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

        private static void WriteDocumentHeader(StringBuilder sb, ErdGraph graph, int tableCount)
        {
            sb.Append("# ").Append(Esc(graph.Title ?? "Dataverse data model"))
              .AppendLine(" — Dataverse data model");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(graph.Subtitle))
                sb.Append("Solution: ").AppendLine(Esc(graph.Subtitle));
            sb.Append("Tables documented: ").AppendLine(tableCount.ToString());
            sb.Append("Generated: ").AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine();
            sb.AppendLine(
                "This describes the tables (entities), columns and relationships of a Microsoft " +
                "Dataverse solution. Each table is documented on its own and is self-contained. " +
                "Relationships are listed on both tables they connect, so either table can answer " +
                "a question about the link. Names are given in two forms: the display name people " +
                "use in the app, and the logical (schema) name used in code, Web API calls and " +
                "FetchXML.");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        /// <summary>
        /// A short orientation section. An agent asked "what is this system
        /// built around?" needs the hubs named somewhere retrievable.
        /// </summary>
        private static void WriteOverview(StringBuilder sb, ErdGraph graph,
            List<ErdNode> tables, bool perTableFiles)
        {
            sb.AppendLine("## Model overview");
            sb.AppendLine();

            if (perTableFiles)
            {
                sb.AppendLine(
                    "Each table in this model is documented in its own file in this folder, named " +
                    "after the table's logical name. This file lists what exists and which tables " +
                    "are central.");
                sb.AppendLine();
            }

            var degree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in tables) degree[t.Id] = 0;
            foreach (var e in graph.Edges)
            {
                if (degree.ContainsKey(e.FromId)) degree[e.FromId]++;
                if (degree.ContainsKey(e.ToId) && !e.IsSelf) degree[e.ToId]++;
            }

            var hubs = tables
                .OrderByDescending(t => degree[t.Id])
                .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .Where(t => degree[t.Id] > 0)
                .ToList();

            if (hubs.Count > 0)
            {
                sb.AppendLine("The most connected tables in this model, which act as its hubs:");
                sb.AppendLine();
                foreach (var hub in hubs)
                    sb.Append("- **").Append(Esc(hub.Title)).Append("** (`").Append(hub.Id)
                      .Append("`) — ").Append(degree[hub.Id]).AppendLine(" relationships");
                sb.AppendLine();
            }

            sb.AppendLine("All tables covered:");
            sb.AppendLine();
            foreach (var t in tables)
                sb.Append("- **").Append(Esc(t.Title)).Append("** (`").Append(t.Id).AppendLine("`)");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        private static void WriteTable(StringBuilder sb, ErdGraph graph, ErdNode node,
            int headingLevel, string solutionName = null, string solutionDetail = null)
        {
            var entity = node.Entity;
            var name = Esc(entity.DisplayName ?? entity.LogicalName);
            var h = new string('#', headingLevel);
            var sub = new string('#', headingLevel + 1);

            sb.Append(h).Append(' ').Append(name)
              .Append(" (`").Append(entity.LogicalName).AppendLine("`)");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(solutionName))
            {
                // Provenance travels with the chunk: a retrieved passage should
                // say which solution and version it describes, using the name
                // people actually say rather than the unique name.
                sb.Append("Part of the ").Append(Esc(solutionName)).Append(" Dataverse solution");
                if (!string.IsNullOrEmpty(solutionDetail))
                    sb.Append(" (").Append(Esc(solutionDetail)).Append(')');
                sb.Append(". Documented ").Append(DateTime.Now.ToString("yyyy-MM-dd")).AppendLine(".");
                sb.AppendLine();
            }

            // Identity sentence — deliberately repeats the table name so the
            // chunk stands alone when retrieved without its heading.
            sb.Append("**").Append(name).Append("** is a ");
            sb.Append(entity.IsActivity ? "activity" : entity.IsCustom ? "custom" : "standard");
            sb.Append(" table. Its logical name is `").Append(entity.LogicalName).Append("`");
            if (!string.IsNullOrEmpty(entity.SchemaName) &&
                !string.Equals(entity.SchemaName, entity.LogicalName, StringComparison.OrdinalIgnoreCase))
                sb.Append(" (schema name `").Append(entity.SchemaName).Append("`)");
            sb.AppendLine(".");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(entity.PrimaryIdAttribute))
                sb.Append("- Primary key column of ").Append(name).Append(": `")
                  .Append(entity.PrimaryIdAttribute).AppendLine("`");
            if (!string.IsNullOrEmpty(entity.PrimaryNameAttribute))
                sb.Append("- Primary name column of ").Append(name).Append(": `")
                  .Append(entity.PrimaryNameAttribute).AppendLine("`");
            if (!string.IsNullOrEmpty(entity.OwnershipType))
                sb.Append("- Ownership of ").Append(name).Append(": ").AppendLine(entity.OwnershipType);
            if (!string.IsNullOrEmpty(entity.Description))
                sb.Append("- Description: ").AppendLine(Esc(entity.Description));
            sb.AppendLine();

            WriteColumns(sb, entity, name, sub);
            WriteRelationships(sb, graph, node, name, sub);
        }

        /// <summary>
        /// Columns as self-describing bullets. A Markdown table would be more
        /// compact, but a chunk boundary inside one strands rows from their
        /// header and the model has to guess what each cell meant.
        /// </summary>
        private static void WriteColumns(StringBuilder sb, EntityModel entity, string name, string sub)
        {
            if (entity.Attributes.Count == 0) return;

            sb.Append(sub).Append(" Columns of ").AppendLine(name);
            sb.AppendLine();

            foreach (var attr in entity.Attributes
                .OrderBy(a => a.IsPrimaryId ? 0 : a.IsPrimaryName ? 1 : a.IsLookup ? 2 : 3)
                .ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append("- **").Append(Esc(attr.DisplayName ?? attr.LogicalName))
                  .Append("** (`").Append(attr.LogicalName).Append("`) — ");

                if (attr.Targets.Count > 0)
                    sb.Append("Lookup to ")
                      .Append(string.Join(", ", attr.Targets.Select(t => "`" + t + "`")));
                else
                    sb.Append(Esc(string.IsNullOrEmpty(attr.TypeLabel) ? "Column" : attr.TypeLabel));
                sb.Append('.');

                if (attr.IsPrimaryId) sb.Append(" Primary key of ").Append(name).Append('.');
                else if (attr.IsPrimaryName) sb.Append(" Primary name of ").Append(name).Append('.');

                var required = RequiredLabel(attr.RequiredLevel);
                if (required.Length > 0) sb.Append(' ').Append(required).Append('.');

                sb.AppendLine();
            }
            sb.AppendLine();
        }

        private static void WriteRelationships(StringBuilder sb, ErdGraph graph, ErdNode node,
            string name, string sub)
        {
            var lines = new List<string>();

            foreach (var edge in graph.Edges)
            {
                bool isFrom = string.Equals(edge.FromId, node.Id, StringComparison.OrdinalIgnoreCase);
                bool isTo = string.Equals(edge.ToId, node.Id, StringComparison.OrdinalIgnoreCase);
                if (!isFrom && !isTo) continue;

                var rel = edge.Relationship;
                if (rel == null) continue;

                var otherId = isFrom ? edge.ToId : edge.FromId;
                var other = graph[otherId];
                var otherName = Esc(other?.Title ?? otherId);
                var lookup = Esc(rel.LookupDisplayName ?? rel.LookupAttribute ?? "");
                var schema = "`" + rel.SchemaName + "`";

                if (edge.IsSelf)
                {
                    lines.Add($"- **{name}** references itself through the lookup column " +
                              $"**{lookup}** (`{rel.LookupAttribute}`). Relationship {schema}.");
                }
                else if (rel.Kind == RelationshipKind.ManyToMany)
                {
                    lines.Add($"- **{name}** has a many-to-many relationship with **{otherName}** " +
                              $"(`{otherId}`), through the intersect table `{rel.IntersectEntity}`. " +
                              $"Relationship {schema}.");
                }
                else if (isFrom)
                {
                    // This table is the referenced ("one") side.
                    lines.Add($"- **{name}** is referenced by **{otherName}** (`{otherId}`) through " +
                              $"{otherName}'s lookup column **{lookup}** (`{rel.LookupAttribute}`). " +
                              $"One {name} record can have many {otherName} records. Relationship {schema}.");
                }
                else
                {
                    // This table is the referencing ("many") side.
                    lines.Add($"- **{name}** references **{otherName}** (`{otherId}`) through " +
                              $"{name}'s lookup column **{lookup}** (`{rel.LookupAttribute}`). " +
                              $"Many {name} records can point to one {otherName} record. Relationship {schema}.");
                }
            }

            sb.Append(sub).Append(" Relationships of ").AppendLine(name);
            sb.AppendLine();
            if (lines.Count == 0)
            {
                sb.Append("**").Append(name)
                  .AppendLine("** has no relationships to other tables in this model.");
            }
            else
            {
                foreach (var line in lines.Distinct().OrderBy(l => l, StringComparer.OrdinalIgnoreCase))
                    sb.AppendLine(line);
            }
            sb.AppendLine();
        }

        private static string RequiredLabel(string level)
        {
            switch (level)
            {
                case "SystemRequired": return "System required";
                case "ApplicationRequired": return "Required";
                case "Recommended": return "Recommended";
                case "None": return "Optional";
                default: return "";
            }
        }

        /// <summary>
        /// Keeps table cells and bold runs from breaking on stray markup.
        /// Underscores are deliberately NOT escaped: they appear in nearly every
        /// Dataverse name, CommonMark does not emphasise them inside a word, and
        /// escaping them would add noise to every retrieved chunk.
        /// </summary>
        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("|", "\\|").Replace("*", "\\*");
        }
    }
}
