using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DataverseErdVisualizer.Models;

namespace DataverseErdVisualizer.Exporters
{
    /// <summary>
    /// Writes the model as a Markdown knowledge base for grounding an AI agent
    /// (Copilot Studio and similar), rather than as a document to read.
    ///
    /// Retrieval, not layout, drives every choice here:
    ///   • one "##" section per table, so chunkers split on table boundaries;
    ///   • each section names its table in full instead of saying "it", because
    ///     a chunk is retrieved without the sections around it;
    ///   • relationships are written as sentences from BOTH sides, so a question
    ///     about either table matches — a lookup listed only on the child would
    ///     never surface when asking what references the parent;
    ///   • no diagram is embedded: image geometry would swamp every chunk.
    /// </summary>
    public static class MarkdownExporter
    {
        public static void Save(ErdDiagram diagram, string path)
            => File.WriteAllText(path, Generate(diagram), new UTF8Encoding(false));

        public static string Generate(ErdDiagram diagram)
        {
            var graph = diagram.Graph;
            var tables = graph.Nodes
                .Where(n => n.Entity != null && !n.Entity.IsExternal)
                .OrderBy(n => n.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sb = new StringBuilder();
            WriteHeader(sb, graph, tables);
            WriteOverview(sb, graph, tables);

            foreach (var node in tables)
                WriteTable(sb, graph, node);

            return sb.ToString();
        }

        private static void WriteHeader(StringBuilder sb, ErdGraph graph, List<ErdNode> tables)
        {
            sb.Append("# ").Append(graph.Title ?? "Dataverse data model")
              .AppendLine(" — Dataverse data model");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(graph.Subtitle))
                sb.Append("Solution: ").AppendLine(graph.Subtitle);
            sb.Append("Tables documented: ").AppendLine(tables.Count.ToString());
            sb.Append("Generated: ").AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine();
            sb.AppendLine(
                "This document describes the tables (entities), columns and relationships of a " +
                "Microsoft Dataverse solution. Each section below covers exactly one table and is " +
                "self-contained. Relationships are listed on both tables they connect, so either " +
                "table can answer a question about the link. Names are given in two forms: the " +
                "display name people use in the app, and the logical (schema) name used in code, " +
                "Web API calls and FetchXML.");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        /// <summary>
        /// A short orientation section. An agent asked "what is this system
        /// built around?" needs the hubs named somewhere retrievable.
        /// </summary>
        private static void WriteOverview(StringBuilder sb, ErdGraph graph, List<ErdNode> tables)
        {
            sb.AppendLine("## Model overview");
            sb.AppendLine();

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

            sb.AppendLine("All tables covered by this document:");
            sb.AppendLine();
            sb.AppendLine(string.Join(", ",
                tables.Select(t => Esc(t.Title) + " (`" + t.Id + "`)")));
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        private static void WriteTable(StringBuilder sb, ErdGraph graph, ErdNode node)
        {
            var entity = node.Entity;
            var name = Esc(entity.DisplayName ?? entity.LogicalName);

            sb.Append("## ").Append(name).Append(" (`").Append(entity.LogicalName).AppendLine("`)");
            sb.AppendLine();

            // Identity sentence — deliberately repeats the table name so the
            // chunk stands alone when retrieved without its heading.
            sb.Append("**").Append(name).Append("** is a ");
            sb.Append(entity.IsActivity ? "activity" : entity.IsCustom ? "custom" : "standard");
            sb.Append(" table in this solution. Its logical name is `").Append(entity.LogicalName).Append("`");
            if (!string.IsNullOrEmpty(entity.SchemaName) &&
                !string.Equals(entity.SchemaName, entity.LogicalName, StringComparison.OrdinalIgnoreCase))
                sb.Append(" (schema name `").Append(entity.SchemaName).Append("`)");
            sb.AppendLine(".");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(entity.PrimaryIdAttribute))
                sb.Append("- Primary key column: `").Append(entity.PrimaryIdAttribute).AppendLine("`");
            if (!string.IsNullOrEmpty(entity.PrimaryNameAttribute))
                sb.Append("- Primary name column: `").Append(entity.PrimaryNameAttribute).AppendLine("`");
            if (!string.IsNullOrEmpty(entity.OwnershipType))
                sb.Append("- Ownership: ").AppendLine(entity.OwnershipType);
            if (!string.IsNullOrEmpty(entity.Description))
                sb.Append("- Description: ").AppendLine(Esc(entity.Description));
            sb.AppendLine();

            WriteColumns(sb, entity, name);
            WriteRelationships(sb, graph, node, name);

            sb.AppendLine("---");
            sb.AppendLine();
        }

        private static void WriteColumns(StringBuilder sb, EntityModel entity, string name)
        {
            if (entity.Attributes.Count == 0) return;

            sb.Append("### Columns of ").AppendLine(name);
            sb.AppendLine();
            sb.AppendLine("| Column | Logical name | Type | Required | Lookup target |");
            sb.AppendLine("| --- | --- | --- | --- | --- |");

            foreach (var attr in entity.Attributes
                .OrderBy(a => a.IsPrimaryId ? 0 : a.IsPrimaryName ? 1 : a.IsLookup ? 2 : 3)
                .ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                var marker = attr.IsPrimaryId ? " (primary key)"
                           : attr.IsPrimaryName ? " (primary name)" : "";
                sb.Append("| ").Append(Esc(attr.DisplayName ?? attr.LogicalName)).Append(marker)
                  .Append(" | `").Append(attr.LogicalName)
                  .Append("` | ").Append(Esc(attr.TypeLabel))
                  .Append(" | ").Append(RequiredLabel(attr.RequiredLevel))
                  .Append(" | ").Append(attr.Targets.Count > 0
                      ? string.Join(", ", attr.Targets.Select(t => "`" + t + "`"))
                      : "")
                  .AppendLine(" |");
            }
            sb.AppendLine();
        }

        private static void WriteRelationships(StringBuilder sb, ErdGraph graph, ErdNode node, string name)
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

            sb.Append("### Relationships of ").AppendLine(name);
            sb.AppendLine();
            if (lines.Count == 0)
            {
                sb.Append("**").Append(name)
                  .AppendLine("** has no relationships to other tables in this document.");
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
                default: return level ?? "";
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
