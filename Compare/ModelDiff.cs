using System;
using System.Collections.Generic;
using System.Linq;
using DataverseErdVisualizer.Models;

namespace DataverseErdVisualizer.Compare
{
    public enum ChangeKind
    {
        Added,
        Removed,
        Changed
    }

    /// <summary>One property that differs, in words ready to show a person.</summary>
    public class PropertyChange
    {
        public string Property { get; set; }

        /// <summary>Null when the property did not exist on the baseline side.</summary>
        public string Before { get; set; }

        /// <summary>Null when the property no longer exists on the current side.</summary>
        public string After { get; set; }
    }

    public class ColumnChange
    {
        public ChangeKind Kind { get; set; }
        public string LogicalName { get; set; }
        public string DisplayName { get; set; }
        public string TypeLabel { get; set; }
        public List<PropertyChange> Properties { get; } = new List<PropertyChange>();
    }

    public class TableChange
    {
        public ChangeKind Kind { get; set; }
        public string LogicalName { get; set; }
        public string DisplayName { get; set; }
        public bool IsCustom { get; set; }
        public int ColumnCount { get; set; }
        public List<PropertyChange> Properties { get; } = new List<PropertyChange>();
        public List<ColumnChange> Columns { get; } = new List<ColumnChange>();
    }

    public class RelationshipChange
    {
        public ChangeKind Kind { get; set; }
        public string SchemaName { get; set; }
        public RelationshipKind RelationshipKind { get; set; }
        public string ReferencedEntity { get; set; }
        public string ReferencedDisplayName { get; set; }
        public string ReferencingEntity { get; set; }
        public string ReferencingDisplayName { get; set; }
        public string LookupAttribute { get; set; }
        public string IntersectEntity { get; set; }

        /// <summary>
        /// Platform plumbing (owner, created-by, currency…) — the same test the
        /// diagram uses to hide noise. Kept, but reported separately: a platform
        /// update can add such a lookup to every table at once.
        /// </summary>
        public bool IsSystem { get; set; }

        public CascadeModel CascadeBefore { get; set; }
        public CascadeModel CascadeAfter { get; set; }
        public List<PropertyChange> Properties { get; } = new List<PropertyChange>();
    }

    /// <summary>Everything that differs between two data models.</summary>
    public class ErdDiff
    {
        public List<TableChange> Tables { get; } = new List<TableChange>();
        public List<RelationshipChange> Relationships { get; } = new List<RelationshipChange>();

        public bool HasChanges => Tables.Count > 0 || Relationships.Count > 0;
    }

    /// <summary>
    /// Compares two data models — typically one solution in two environments,
    /// or before and after a release — and says what was added, removed and
    /// changed at the level of tables, columns, choice values and
    /// relationships.
    ///
    /// Tables are matched by logical name, columns by logical name within their
    /// table, choice values by their stored number, and relationships by schema
    /// name. Display names are compared too, so a relabel shows up — but so do
    /// differences caused by the two sides being read in different user
    /// languages (see the report's notes).
    /// </summary>
    public static class ModelDiff
    {
        public static ErdDiff Compare(ErdModel baseline, ErdModel current)
        {
            var diff = new ErdDiff();
            var names = DisplayNames(baseline, current);

            CompareTables(Tables(baseline), Tables(current), diff);
            CompareRelationships(Relationships(baseline), Relationships(current), names, diff);
            return diff;
        }

        // ------------------------------------------------------------- tables

        /// <summary>
        /// The solution's own tables. External stubs are only relationship
        /// endpoints, and intersect tables are the plumbing of N:N
        /// relationships — both are covered by the relationship comparison.
        /// </summary>
        private static Dictionary<string, EntityModel> Tables(ErdModel model)
        {
            var result = new Dictionary<string, EntityModel>(StringComparer.OrdinalIgnoreCase);
            if (model == null) return result;
            foreach (var e in model.Entities)
            {
                if (e.IsExternal || e.IsIntersect || string.IsNullOrEmpty(e.LogicalName)) continue;
                if (!result.ContainsKey(e.LogicalName)) result[e.LogicalName] = e;
            }
            return result;
        }

        private static void CompareTables(Dictionary<string, EntityModel> before,
            Dictionary<string, EntityModel> after, ErdDiff diff)
        {
            foreach (var name in before.Keys.Union(after.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => (after.ContainsKey(n) ? after[n] : before[n]).DisplayName ?? n,
                    StringComparer.OrdinalIgnoreCase))
            {
                EntityModel b, a;
                before.TryGetValue(name, out b);
                after.TryGetValue(name, out a);

                if (b == null)
                {
                    diff.Tables.Add(TableSummary(a, ChangeKind.Added));
                    continue;
                }
                if (a == null)
                {
                    diff.Tables.Add(TableSummary(b, ChangeKind.Removed));
                    continue;
                }

                var change = TableSummary(a, ChangeKind.Changed);
                Compare(change.Properties, "Display name", b.DisplayName, a.DisplayName);
                Compare(change.Properties, "Description", b.Description, a.Description);
                Compare(change.Properties, "Ownership", b.OwnershipType, a.OwnershipType);
                Compare(change.Properties, "Primary name column", b.PrimaryNameAttribute, a.PrimaryNameAttribute);
                CompareColumns(b, a, change.Columns);

                if (change.Properties.Count > 0 || change.Columns.Count > 0)
                    diff.Tables.Add(change);
            }
        }

        private static TableChange TableSummary(EntityModel e, ChangeKind kind) => new TableChange
        {
            Kind = kind,
            LogicalName = e.LogicalName,
            DisplayName = e.DisplayName ?? e.LogicalName,
            IsCustom = e.IsCustom,
            ColumnCount = e.Attributes.Count
        };

        // ------------------------------------------------------------ columns

        private static void CompareColumns(EntityModel before, EntityModel after, List<ColumnChange> into)
        {
            var b = ByName(before.Attributes);
            var a = ByName(after.Attributes);

            foreach (var name in b.Keys.Union(a.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => (a.ContainsKey(n) ? a[n] : b[n]).DisplayName ?? n, StringComparer.OrdinalIgnoreCase))
            {
                AttributeModel was, now;
                b.TryGetValue(name, out was);
                a.TryGetValue(name, out now);

                if (was == null) { into.Add(ColumnSummary(now, ChangeKind.Added)); continue; }
                if (now == null) { into.Add(ColumnSummary(was, ChangeKind.Removed)); continue; }

                var change = ColumnSummary(now, ChangeKind.Changed);
                Compare(change.Properties, "Display name", was.DisplayName, now.DisplayName);

                // A lookup's type label embeds its first target, so a target
                // change would otherwise be reported twice.
                if (was.IsLookup && now.IsLookup)
                    Compare(change.Properties, "Lookup targets", Targets(was), Targets(now));
                else
                    Compare(change.Properties, "Type", was.TypeLabel, now.TypeLabel);

                Compare(change.Properties, "Required", Required(was.RequiredLevel), Required(now.RequiredLevel));
                Compare(change.Properties, "Description", was.Description, now.Description);
                CompareOptions(was, now, change.Properties);

                if (change.Properties.Count > 0) into.Add(change);
            }
        }

        private static Dictionary<string, AttributeModel> ByName(IEnumerable<AttributeModel> attributes)
        {
            var result = new Dictionary<string, AttributeModel>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in attributes)
                if (!string.IsNullOrEmpty(a.LogicalName) && !result.ContainsKey(a.LogicalName))
                    result[a.LogicalName] = a;
            return result;
        }

        private static ColumnChange ColumnSummary(AttributeModel a, ChangeKind kind) => new ColumnChange
        {
            Kind = kind,
            LogicalName = a.LogicalName,
            DisplayName = a.DisplayName ?? a.LogicalName,
            TypeLabel = a.TypeLabel
        };

        /// <summary>
        /// Choice values are matched by their stored number, which is what
        /// flows and queries depend on: a new number is an addition, a missing
        /// one a removal, and the same number with a different label a relabel.
        /// </summary>
        private static void CompareOptions(AttributeModel before, AttributeModel after, List<PropertyChange> into)
        {
            var b = before.Options.GroupBy(o => o.Value).ToDictionary(g => g.Key, g => g.First());
            var a = after.Options.GroupBy(o => o.Value).ToDictionary(g => g.Key, g => g.First());

            foreach (var value in b.Keys.Union(a.Keys).OrderBy(v => v))
            {
                OptionModel was, now;
                b.TryGetValue(value, out was);
                a.TryGetValue(value, out now);

                var property = "Allowed value " + value;
                if (was == null) into.Add(new PropertyChange { Property = property, After = OptionText(now) });
                else if (now == null) into.Add(new PropertyChange { Property = property, Before = OptionText(was) });
                else Compare(into, property, OptionText(was), OptionText(now));
            }
        }

        private static string OptionText(OptionModel o)
            => string.IsNullOrEmpty(o.StateLabel) ? o.Label : o.Label + " (status " + o.StateLabel + ")";

        private static string Targets(AttributeModel a)
            => string.Join(", ", a.Targets.OrderBy(t => t, StringComparer.OrdinalIgnoreCase));

        private static string Required(string level)
        {
            switch (level)
            {
                case "SystemRequired": return "System required";
                case "ApplicationRequired": return "Required";
                case "Recommended": return "Recommended";
                case "None": return "Optional";
                default: return level;
            }
        }

        // ------------------------------------------------------ relationships

        private static Dictionary<string, RelationshipModel> Relationships(ErdModel model)
        {
            var result = new Dictionary<string, RelationshipModel>(StringComparer.OrdinalIgnoreCase);
            if (model == null) return result;

            // Each 1:N arrives once from each table's collection; count it once.
            foreach (var r in ErdGraphBuilder.DedupeRelationships(model.Relationships))
            {
                var key = r.SchemaName ?? (r.ReferencedEntity + "|" + r.ReferencingEntity + "|" + r.LookupAttribute);
                if (!result.ContainsKey(key)) result[key] = r;
            }
            return result;
        }

        private static void CompareRelationships(Dictionary<string, RelationshipModel> before,
            Dictionary<string, RelationshipModel> after, Dictionary<string, string> names, ErdDiff diff)
        {
            foreach (var key in before.Keys.Union(after.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                RelationshipModel b, a;
                before.TryGetValue(key, out b);
                after.TryGetValue(key, out a);

                if (b == null) { diff.Relationships.Add(RelationshipSummary(a, ChangeKind.Added, names)); continue; }
                if (a == null) { diff.Relationships.Add(RelationshipSummary(b, ChangeKind.Removed, names)); continue; }

                var change = RelationshipSummary(a, ChangeKind.Changed, names);
                change.CascadeBefore = b.Cascade;
                change.CascadeAfter = a.Cascade;

                Compare(change.Properties, "Kind", KindText(b.Kind), KindText(a.Kind));
                Compare(change.Properties, "Parent table", b.ReferencedEntity, a.ReferencedEntity);
                Compare(change.Properties, "Child table", b.ReferencingEntity, a.ReferencingEntity);
                Compare(change.Properties, "Lookup column", b.LookupAttribute, a.LookupAttribute);
                Compare(change.Properties, "Intersect table", b.IntersectEntity, a.IntersectEntity);

                // Unknown is not different: a side that did not record cascade
                // settings says nothing about whether they changed.
                if (b.Cascade != null && a.Cascade != null)
                {
                    Compare(change.Properties, "Cascade on delete", b.Cascade.Delete, a.Cascade.Delete);
                    Compare(change.Properties, "Cascade on assign", b.Cascade.Assign, a.Cascade.Assign);
                    Compare(change.Properties, "Cascade on share", b.Cascade.Share, a.Cascade.Share);
                    Compare(change.Properties, "Cascade on unshare", b.Cascade.Unshare, a.Cascade.Unshare);
                    Compare(change.Properties, "Cascade on reparent", b.Cascade.Reparent, a.Cascade.Reparent);
                }

                if (change.Properties.Count > 0) diff.Relationships.Add(change);
            }
        }

        private static RelationshipChange RelationshipSummary(RelationshipModel r, ChangeKind kind,
            Dictionary<string, string> names) => new RelationshipChange
        {
            Kind = kind,
            SchemaName = r.SchemaName,
            RelationshipKind = r.Kind,
            ReferencedEntity = r.ReferencedEntity,
            ReferencedDisplayName = NameOf(names, r.ReferencedEntity),
            ReferencingEntity = r.ReferencingEntity,
            ReferencingDisplayName = NameOf(names, r.ReferencingEntity),
            LookupAttribute = r.LookupAttribute,
            IntersectEntity = r.IntersectEntity,
            IsSystem = ErdGraphBuilder.IsSystemRelationship(r),
            CascadeBefore = kind == ChangeKind.Removed ? r.Cascade : null,
            CascadeAfter = kind == ChangeKind.Removed ? null : r.Cascade
        };

        private static string KindText(RelationshipKind kind)
            => kind == RelationshipKind.ManyToMany ? "Many-to-many" : "One-to-many";

        /// <summary>Display names from either side, preferring the current one.</summary>
        private static Dictionary<string, string> DisplayNames(ErdModel baseline, ErdModel current)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var model in new[] { baseline, current })
            {
                if (model == null) continue;
                foreach (var e in model.Entities)
                    if (!string.IsNullOrEmpty(e.LogicalName) && !string.IsNullOrEmpty(e.DisplayName))
                        result[e.LogicalName] = e.DisplayName;
            }
            return result;
        }

        private static string NameOf(Dictionary<string, string> names, string logical)
        {
            if (string.IsNullOrEmpty(logical)) return logical;
            string name;
            return names.TryGetValue(logical, out name) ? name : logical;
        }

        // ------------------------------------------------------------- shared

        /// <summary>
        /// Records a difference. Missing and empty are treated as the same, so
        /// a description that was never set does not count as changed.
        /// </summary>
        private static void Compare(List<PropertyChange> into, string property, string before, string after)
        {
            var b = Normalise(before);
            var a = Normalise(after);
            if (string.Equals(b, a, StringComparison.Ordinal)) return;
            into.Add(new PropertyChange
            {
                Property = property,
                Before = b.Length == 0 ? null : b,
                After = a.Length == 0 ? null : a
            });
        }

        private static string Normalise(string s) => (s ?? "").Trim();
    }
}
