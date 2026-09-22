using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DataverseErdVisualizer.Exporters;
using DataverseErdVisualizer.Models;

namespace DataverseErdVisualizer.Compare
{
    /// <summary>
    /// Writes a comparison as a Markdown change report — the thing that gets
    /// pasted into a release pull request, a wiki page or a change ticket.
    ///
    /// Structural changes (tables, columns, relationships) lead; relabels and
    /// descriptions sit alongside them in context; platform plumbing goes in its
    /// own trailing section, because one platform update can touch every table
    /// and would otherwise bury the solution's own changes.
    /// </summary>
    public static class DiffReport
    {
        public static void Save(ModelSnapshot baseline, ModelSnapshot current, ErdDiff diff, string path)
            => File.WriteAllText(path, Generate(baseline, current, diff), new UTF8Encoding(false));

        public static string Generate(ModelSnapshot baseline, ModelSnapshot current, ErdDiff diff)
        {
            var sb = new StringBuilder();
            var title = current?.Model?.Solution?.FriendlyName
                        ?? baseline?.Model?.Solution?.FriendlyName
                        ?? "Dataverse";

            sb.Append("# Data model changes — ").AppendLine(Esc(title));
            sb.AppendLine();
            WriteSides(sb, baseline, current);
            WriteSummary(sb, diff);

            if (diff.HasChanges)
            {
                WriteTables(sb, diff);
                WriteRelationships(sb, diff.Relationships.Where(r => !r.IsSystem).ToList(), system: false);
                WriteRelationships(sb, diff.Relationships.Where(r => r.IsSystem).ToList(), system: true);
            }

            WriteNotes(sb);
            return sb.ToString();
        }

        // --------------------------------------------------------------- head

        private static void WriteSides(StringBuilder sb, ModelSnapshot baseline, ModelSnapshot current)
        {
            sb.AppendLine("| | Baseline | Current |");
            sb.AppendLine("| --- | --- | --- |");
            sb.Append("| Source | ").Append(Esc(baseline?.Environment ?? "—"))
              .Append(" | ").Append(Esc(current?.Environment ?? "—")).AppendLine(" |");
            sb.Append("| Solution | ").Append(Esc(SolutionText(baseline)))
              .Append(" | ").Append(Esc(SolutionText(current))).AppendLine(" |");
            sb.Append("| Captured | ").Append(When(baseline))
              .Append(" | ").Append(When(current)).AppendLine(" |");
            sb.AppendLine();

            var b = baseline?.Model?.Solution?.UniqueName;
            var c = current?.Model?.Solution?.UniqueName;
            if (!string.IsNullOrEmpty(b) && !string.IsNullOrEmpty(c) &&
                !string.Equals(b, c, StringComparison.OrdinalIgnoreCase))
            {
                sb.Append("> **These are two different solutions** (`").Append(b).Append("` and `").Append(c)
                  .AppendLine("`). Every table that belongs to only one of them is reported as added or removed.");
                sb.AppendLine();
            }
        }

        private static string SolutionText(ModelSnapshot s)
        {
            var solution = s?.Model?.Solution;
            if (solution == null) return "—";
            var text = solution.UniqueName ?? solution.FriendlyName ?? "—";
            if (!string.IsNullOrEmpty(solution.Version)) text += " v" + solution.Version;
            return text + (solution.IsManaged ? " (managed)" : " (unmanaged)");
        }

        private static string When(ModelSnapshot s)
            => s == null || s.CapturedOn == default(DateTime)
                ? "—"
                : s.CapturedOn.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

        private static void WriteSummary(StringBuilder sb, ErdDiff diff)
        {
            sb.AppendLine("## Summary");
            sb.AppendLine();

            if (!diff.HasChanges)
            {
                sb.AppendLine("No differences: the two data models match.");
                sb.AppendLine();
                return;
            }

            sb.Append("- Tables: ").AppendLine(Counts(diff.Tables.Select(t => t.Kind)));
            sb.Append("- Relationships: ")
              .AppendLine(Counts(diff.Relationships.Where(r => !r.IsSystem).Select(r => r.Kind)));

            var system = diff.Relationships.Where(r => r.IsSystem).ToList();
            if (system.Count > 0)
                sb.Append("- System relationships: ").Append(Counts(system.Select(r => r.Kind)))
                  .AppendLine(" (listed separately at the end)");
            sb.AppendLine();
        }

        private static string Counts(IEnumerable<ChangeKind> kinds)
        {
            var list = kinds.ToList();
            if (list.Count == 0) return "no changes";
            return string.Join(", ", new[] { ChangeKind.Added, ChangeKind.Removed, ChangeKind.Changed }
                .Where(k => list.Contains(k))
                .Select(k => "**" + list.Count(x => x == k) + " " + k.ToString().ToLowerInvariant() + "**"));
        }

        // ------------------------------------------------------------- tables

        private static void WriteTables(StringBuilder sb, ErdDiff diff)
        {
            var added = diff.Tables.Where(t => t.Kind == ChangeKind.Added).ToList();
            if (added.Count > 0)
            {
                sb.AppendLine("## Tables added");
                sb.AppendLine();
                foreach (var t in added) sb.AppendLine(TableLine(t));
                sb.AppendLine();
            }

            var removed = diff.Tables.Where(t => t.Kind == ChangeKind.Removed).ToList();
            if (removed.Count > 0)
            {
                sb.AppendLine("## Tables removed");
                sb.AppendLine();
                foreach (var t in removed) sb.AppendLine(TableLine(t));
                sb.AppendLine();
            }

            var changed = diff.Tables.Where(t => t.Kind == ChangeKind.Changed).ToList();
            if (changed.Count == 0) return;

            sb.AppendLine("## Tables changed");
            sb.AppendLine();
            foreach (var t in changed)
            {
                sb.Append("### ").Append(Esc(t.DisplayName)).Append(" (`").Append(t.LogicalName).AppendLine("`)");
                sb.AppendLine();
                foreach (var p in t.Properties) sb.Append("- ").AppendLine(PropertyText(p));

                foreach (var c in t.Columns.OrderBy(c => c.Kind))
                {
                    var name = "**" + Esc(c.DisplayName) + "** (`" + c.LogicalName + "`)";
                    switch (c.Kind)
                    {
                        case ChangeKind.Added:
                            sb.Append("- Column added: ").Append(name).Append(TypeSuffix(c)).AppendLine();
                            break;
                        case ChangeKind.Removed:
                            sb.Append("- Column removed: ").Append(name).Append(TypeSuffix(c)).AppendLine();
                            break;
                        default:
                            sb.Append("- Column changed: ").AppendLine(name);
                            foreach (var p in c.Properties) sb.Append("  - ").AppendLine(PropertyText(p));
                            break;
                    }
                }
                sb.AppendLine();
            }
        }

        private static string TableLine(TableChange t)
            => "- **" + Esc(t.DisplayName) + "** (`" + t.LogicalName + "`) — " +
               (t.IsCustom ? "custom table, " : "") + t.ColumnCount + (t.ColumnCount == 1 ? " column" : " columns");

        private static string TypeSuffix(ColumnChange c)
            => string.IsNullOrEmpty(c.TypeLabel) ? "" : " — " + Esc(c.TypeLabel);

        // ------------------------------------------------------ relationships

        private static void WriteRelationships(StringBuilder sb, List<RelationshipChange> rels, bool system)
        {
            if (rels.Count == 0) return;

            if (system)
            {
                sb.AppendLine("## System relationship changes");
                sb.AppendLine();
                sb.AppendLine("Platform plumbing — owner, created by, modified by, currency and similar. These " +
                              "usually come from platform updates rather than work on the solution.");
                sb.AppendLine();
            }

            foreach (var kind in new[] { ChangeKind.Added, ChangeKind.Removed, ChangeKind.Changed })
            {
                var group = rels.Where(r => r.Kind == kind).ToList();
                if (group.Count == 0) continue;

                sb.Append(system ? "### " : "## ").Append(system ? "" : "Relationships ")
                  .AppendLine(system ? Capitalise(kind.ToString()) : kind.ToString().ToLowerInvariant());
                sb.AppendLine();

                foreach (var r in group)
                {
                    sb.Append("- `").Append(r.SchemaName).Append("` — ").AppendLine(Describe(r));
                    if (kind != ChangeKind.Changed) continue;

                    var before = CascadeText.Name(r.CascadeBefore);
                    var after = CascadeText.Name(r.CascadeAfter);
                    if (before != null && after != null && before != after)
                        sb.Append("  - Behaviour: ").Append(before).Append(" → ").AppendLine(after);

                    foreach (var p in r.Properties) sb.Append("  - ").AppendLine(PropertyText(p));
                }
                sb.AppendLine();
            }
        }

        private static string Describe(RelationshipChange r)
        {
            var from = Esc(r.ReferencedDisplayName ?? r.ReferencedEntity);
            var to = Esc(r.ReferencingDisplayName ?? r.ReferencingEntity);
            if (r.RelationshipKind == RelationshipKind.ManyToMany)
                return from + " ↔ " + to + " (many-to-many" +
                       (string.IsNullOrEmpty(r.IntersectEntity) ? ")" : ", through `" + r.IntersectEntity + "`)");
            return from + " → " + to + " (one-to-many" +
                   (string.IsNullOrEmpty(r.LookupAttribute) ? ")" : ", through `" + r.LookupAttribute + "`)");
        }

        // ------------------------------------------------------------- shared

        private static string PropertyText(PropertyChange p)
        {
            var before = Word(p.Property, p.Before);
            var after = Word(p.Property, p.After);
            if (p.Before == null) return p.Property + ": added \"" + Esc(after) + "\"";
            if (p.After == null) return p.Property + ": removed \"" + Esc(before) + "\"";
            return p.Property + ": \"" + Esc(before) + "\" → \"" + Esc(after) + "\"";
        }

        /// <summary>Cascade settings arrive as SDK names; say what they mean.</summary>
        private static string Word(string property, string value)
        {
            if (value == null || !property.StartsWith("Cascade on ", StringComparison.Ordinal)) return value;
            switch (value)
            {
                case "Cascade": return "cascade to all";
                case "Active": return "cascade to active";
                case "UserOwned": return "cascade to same owner";
                case "NoCascade": return "no cascade";
                case "RemoveLink": return "remove link";
                case "Restrict": return "restrict";
                default: return value;
            }
        }

        private static void WriteNotes(StringBuilder sb)
        {
            sb.AppendLine("## Notes");
            sb.AppendLine();
            sb.AppendLine("- Tables and columns are matched by logical name, choice values by the number they " +
                          "are stored as, and relationships by schema name.");
            sb.AppendLine("- Display names and descriptions are read in the language of the user who captured " +
                          "each side. If the two sides were captured in different languages, labels will show " +
                          "as changed even where nothing was.");
        }

        private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        private static string Esc(string s)
            => string.IsNullOrEmpty(s) ? "" : s.Replace("|", "\\|").Replace("*", "\\*");
    }
}
