using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using DataverseErdVisualizer.Layout;
using DataverseErdVisualizer.Models;
using DataverseErdVisualizer.Rendering;

namespace DataverseErdVisualizer
{
    /// <summary>Which attribute rows an entity box shows.</summary>
    public enum AttributeDisplayMode
    {
        KeysAndLookups,
        CustomOnly,
        All,
        None
    }

    public class ErdOptions
    {
        public AttributeDisplayMode AttributeMode { get; set; } = AttributeDisplayMode.KeysAndLookups;
        public bool IncludeManyToMany { get; set; } = true;
        public bool IncludeSelfReferential { get; set; } = true;
        public bool IncludeExternalEntities { get; set; } = true;

        /// <summary>
        /// Show system plumbing: owner/created/modified lookups, currency,
        /// business unit etc. Off by default — they turn ERDs into spaghetti.
        /// </summary>
        public bool IncludeSystemRelationships { get; set; }

        public bool ShowEdgeLabels { get; set; } = true;

        /// <summary>Cap on attribute rows per box in "All" mode.</summary>
        public int MaxAttributesPerEntity { get; set; } = 40;

        /// <summary>Logical names to render; null/empty = all non-intersect solution tables.</summary>
        public HashSet<string> SelectedEntities { get; set; }
    }

    /// <summary>
    /// Turns the fetched <see cref="ErdModel"/> into a laid-out diagram,
    /// applying the display options (attribute modes, system-noise filters,
    /// external stubs, N:N and self-loop toggles).
    /// </summary>
    public static class ErdGraphBuilder
    {
        /// <summary>Lookup targets that are plumbing on almost every table.</summary>
        private static readonly HashSet<string> SystemEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "systemuser", "team", "businessunit", "organization", "transactioncurrency",
            "principal", "owner", "processstage", "processsession", "workflow",
            "sla", "slakpiinstance", "asyncoperation", "syncerror", "duplicaterule",
            "mailbox", "queue", "queueitem", "postfollow", "importfile", "bulkdeletefailure",
            "principalobjectattributeaccess", "mobileofflineprofileitem", "activitypointer",
            "activityparty", "annotation", "userentityinstancedata", "fileattachment"
        };

        /// <summary>Attribute logical names that are plumbing on almost every table.</summary>
        private static readonly HashSet<string> SystemAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "createdby", "createdonbehalfby", "modifiedby", "modifiedonbehalfby",
            "ownerid", "owninguser", "owningteam", "owningbusinessunit", "organizationid",
            "transactioncurrencyid", "stageid", "processid", "slaid", "slainvokedid",
            "createdon", "modifiedon", "overriddencreatedon", "importsequencenumber",
            "timezoneruleversionnumber", "utcconversiontimezonecode", "versionnumber",
            "statecode", "statuscode", "exchangerate", "entityimageid", "entityimage",
            "traversedpath", "onholdtime", "lastonholdtime"
        };

        /// <summary>Builds, sizes and lays out the diagram in one go.</summary>
        public static ErdDiagram Build(ErdModel model, ErdOptions options, IDiagramSurface measure)
        {
            var graph = BuildGraph(model, options);
            ErdNodeSizer.Size(graph, measure);
            var canvas = ErdLayoutEngine.Layout(graph);
            return new ErdDiagram { Graph = graph, CanvasSize = canvas };
        }

        /// <summary>Builds the unsized graph (exposed separately for tests).</summary>
        public static ErdGraph BuildGraph(ErdModel model, ErdOptions options)
        {
            var graph = new ErdGraph
            {
                Title = model.Solution?.FriendlyName ?? "Entity Relationship Diagram",
                Subtitle = BuildSubtitle(model),
                // Rotated relationship labels climb the connector drops, so
                // give the drops extra length and wider port pitch (a label
                // column is ~16px wide) when labels are on.
                ExtraRankGap = options.ShowEdgeLabels ? 56f : 0f,
                PortSpacing = options.ShowEdgeLabels ? 18f : 14f
            };

            // --- entity boxes ---
            var inScope = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entity in model.Entities)
            {
                if (entity.IsIntersect) continue;           // N:N plumbing → edges, not boxes
                if (entity.IsExternal) continue;            // stubs added on demand below
                if (options.SelectedEntities != null && options.SelectedEntities.Count > 0 &&
                    !options.SelectedEntities.Contains(entity.LogicalName)) continue;

                graph.AddNode(CreateNode(entity, options));
                inScope.Add(entity.LogicalName);
            }

            // --- relationships ---
            var kept = new List<RelationshipModel>();
            var externals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rel in DedupeRelationships(model.Relationships))
            {
                if (rel.Kind == RelationshipKind.ManyToMany && !options.IncludeManyToMany) continue;
                if (rel.IsSelfReferential && !options.IncludeSelfReferential) continue;
                if (!options.IncludeSystemRelationships && IsSystemRelationship(rel)) continue;

                bool fromIn = inScope.Contains(rel.ReferencedEntity);
                bool toIn = inScope.Contains(rel.ReferencingEntity);
                if (!fromIn && !toIn) continue;

                if (!fromIn || !toIn)
                {
                    if (!options.IncludeExternalEntities) continue;
                    externals.Add(fromIn ? rel.ReferencingEntity : rel.ReferencedEntity);
                }

                kept.Add(rel);
            }

            // --- external stub boxes ---
            foreach (var logical in externals.OrderBy(x => x))
            {
                var known = model.Entities.FirstOrDefault(e =>
                    string.Equals(e.LogicalName, logical, StringComparison.OrdinalIgnoreCase));
                var stub = known ?? new EntityModel { LogicalName = logical, DisplayName = logical };
                graph.AddNode(new ErdNode
                {
                    Id = logical,
                    Title = stub.DisplayName ?? logical,
                    Subtitle = logical,
                    Flavor = NodeFlavor.External,
                    Entity = stub
                });
            }

            // --- edges with parallel fanning ---
            var parallelGroups = kept.GroupBy(PairKey);
            foreach (var group in parallelGroups)
            {
                var members = group.ToList();
                for (int i = 0; i < members.Count; i++)
                {
                    var rel = members[i];
                    graph.AddEdge(new ErdEdge
                    {
                        FromId = rel.ReferencedEntity,
                        ToId = rel.ReferencingEntity,
                        Kind = rel.Kind,
                        Relationship = rel,
                        IsSelf = rel.IsSelfReferential,
                        Label = options.ShowEdgeLabels ? EdgeLabel(rel) : null,
                        ParallelIndex = i,
                        ParallelCount = members.Count
                    });
                }
            }

            return graph;
        }

        private static string BuildSubtitle(ErdModel model)
        {
            if (model.Solution == null) return null;
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(model.Solution.UniqueName)) parts.Add(model.Solution.UniqueName);
            if (!string.IsNullOrEmpty(model.Solution.Version)) parts.Add("v" + model.Solution.Version);
            parts.Add(model.Solution.IsManaged ? "managed" : "unmanaged");
            return string.Join(" · ", parts);
        }

        /// <summary>
        /// A 1:N and its mirrored registration (or duplicates across entities'
        /// relationship collections) share a schema name — keep one of each.
        /// </summary>
        private static IEnumerable<RelationshipModel> DedupeRelationships(IEnumerable<RelationshipModel> rels)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rel in rels)
            {
                var key = rel.SchemaName ?? (rel.ReferencedEntity + "|" + rel.ReferencingEntity + "|" + rel.LookupAttribute);
                if (seen.Add(key)) yield return rel;
            }
        }

        private static bool IsSystemRelationship(RelationshipModel rel)
        {
            if (SystemEntities.Contains(rel.ReferencedEntity)) return true;
            if (SystemEntities.Contains(rel.ReferencingEntity)) return true;
            if (rel.LookupAttribute != null && SystemAttributes.Contains(rel.LookupAttribute)) return true;
            return false;
        }

        private static string PairKey(RelationshipModel rel)
        {
            var a = rel.ReferencedEntity ?? "";
            var b = rel.ReferencingEntity ?? "";
            return string.CompareOrdinal(a.ToLowerInvariant(), b.ToLowerInvariant()) <= 0
                ? a.ToLowerInvariant() + "|" + b.ToLowerInvariant()
                : b.ToLowerInvariant() + "|" + a.ToLowerInvariant();
        }

        private static string EdgeLabel(RelationshipModel rel)
        {
            if (rel.Kind == RelationshipKind.ManyToMany)
                return rel.IntersectEntity ?? rel.SchemaName;
            return rel.LookupDisplayName ?? rel.LookupAttribute ?? rel.SchemaName;
        }

        private static ErdNode CreateNode(EntityModel entity, ErdOptions options)
        {
            var node = new ErdNode
            {
                Id = entity.LogicalName,
                Title = entity.DisplayName ?? entity.LogicalName,
                Subtitle = entity.LogicalName,
                Flavor = entity.IsActivity ? NodeFlavor.Activity
                       : entity.IsCustom ? NodeFlavor.Custom
                       : NodeFlavor.Standard,
                Entity = entity
            };

            if (options.AttributeMode == AttributeDisplayMode.None)
                return node;

            var picked = new List<AttributeModel>();
            foreach (var attr in entity.Attributes)
            {
                if (!Include(attr, entity, options)) continue;
                picked.Add(attr);
            }

            // PK first, primary name second, lookups next, the rest alphabetical.
            var sorted = picked
                .OrderBy(a => a.IsPrimaryId ? 0 : a.IsPrimaryName ? 1 : a.IsLookup ? 2 : 3)
                .ThenBy(a => a.DisplayName ?? a.LogicalName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int cap = Math.Max(1, options.MaxAttributesPerEntity);
            if (sorted.Count > cap)
            {
                node.MoreCount = sorted.Count - cap;
                sorted = sorted.Take(cap).ToList();
            }

            foreach (var attr in sorted)
            {
                node.Rows.Add(new ErdRow
                {
                    Badge = attr.IsPrimaryId ? RowBadge.PrimaryKey
                          : attr.IsPrimaryName ? RowBadge.PrimaryName
                          : attr.IsLookup ? RowBadge.Lookup
                          : RowBadge.None,
                    Name = attr.DisplayName ?? attr.LogicalName,
                    Type = attr.TypeLabel
                });
            }

            return node;
        }

        private static bool Include(AttributeModel attr, EntityModel entity, ErdOptions options)
        {
            // Primary key and primary name always show (they define the table).
            if (attr.IsPrimaryId || attr.IsPrimaryName) return true;

            bool isSystem = SystemAttributes.Contains(attr.LogicalName);
            if (isSystem && !options.IncludeSystemRelationships) return false;

            switch (options.AttributeMode)
            {
                case AttributeDisplayMode.KeysAndLookups:
                    return attr.IsLookup;
                case AttributeDisplayMode.CustomOnly:
                    return attr.IsCustom;
                case AttributeDisplayMode.All:
                    return true;
                default:
                    return false;
            }
        }
    }
}
