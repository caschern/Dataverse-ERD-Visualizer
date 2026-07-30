using System.IO;
using System.Linq;
using System.Text;
using DataverseErdVisualizer.Models;

namespace DataverseErdVisualizer.Exporters
{
    /// <summary>
    /// Writes the diagram as a Mermaid <c>erDiagram</c> — pasteable into Azure
    /// DevOps wikis, GitHub markdown, Confluence and mermaid.live.
    /// </summary>
    public static class MermaidExporter
    {
        public static string Generate(ErdDiagram diagram)
        {
            var graph = diagram.Graph;
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(graph.Title))
            {
                sb.AppendLine("---");
                sb.AppendLine("title: " + graph.Title);
                sb.AppendLine("---");
            }
            sb.AppendLine("erDiagram");

            foreach (var edge in graph.Edges)
            {
                var rel = edge.Relationship;
                string label = Quote(edge.Label ?? rel?.SchemaName ?? "");
                if (edge.Kind == RelationshipKind.ManyToMany)
                    sb.AppendLine($"    {Ident(edge.FromId)} }}o--o{{ {Ident(edge.ToId)} : {label}");
                else
                    sb.AppendLine($"    {Ident(edge.FromId)} ||--o{{ {Ident(edge.ToId)} : {label}");
            }

            foreach (var node in graph.Nodes.Where(n => n.Entity != null))
            {
                if (node.Rows.Count == 0) continue;
                sb.AppendLine($"    {Ident(node.Id)} {{");
                foreach (var row in node.Rows)
                {
                    var attr = node.Entity.Attributes.FirstOrDefault(a =>
                        (a.DisplayName ?? a.LogicalName) == row.Name);
                    string type = Word(row.Type) ?? "string";
                    string name = Word(attr?.LogicalName ?? row.Name) ?? "column";
                    string key = row.Badge == RowBadge.PrimaryKey ? " PK"
                               : row.Badge == RowBadge.Lookup ? " FK"
                               : "";
                    string comment = attr != null && attr.DisplayName != attr.LogicalName
                        ? " " + Quote(attr.DisplayName)
                        : "";
                    sb.AppendLine($"        {type} {name}{key}{comment}");
                }
                sb.AppendLine("    }");
            }

            return sb.ToString();
        }

        public static void Save(ErdDiagram diagram, string path)
            => File.WriteAllText(path, Generate(diagram), Encoding.UTF8);

        /// <summary>Mermaid identifiers must be single words.</summary>
        private static string Ident(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unknown";
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            return sb.ToString();
        }

        private static string Word(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
                else if (c == ' ' || c == '/' || c == '(' ) sb.Append('_');
                // other punctuation (')', ':') is dropped
            }
            var word = sb.ToString().Trim('_');
            return word.Length == 0 ? null : word;
        }

        private static string Quote(string s)
            => "\"" + (s ?? "").Replace("\"", "'") + "\"";
    }
}
