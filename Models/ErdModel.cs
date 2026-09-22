using System;
using System.Collections.Generic;

namespace DataverseErdVisualizer.Models
{
    /// <summary>A row from the <c>solution</c> table.</summary>
    public class SolutionInfo
    {
        public Guid Id { get; set; }
        public string UniqueName { get; set; }
        public string FriendlyName { get; set; }
        public string Version { get; set; }
        public string Publisher { get; set; }
        public bool IsManaged { get; set; }
    }

    /// <summary>
    /// A table in the diagram's scope. Deliberately SDK-free so the graph
    /// builder and layout can be unit tested without the CRM assemblies.
    /// </summary>
    public class EntityModel
    {
        public string LogicalName { get; set; }
        public string SchemaName { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string PrimaryIdAttribute { get; set; }
        public string PrimaryNameAttribute { get; set; }
        public string OwnershipType { get; set; }
        public bool IsCustom { get; set; }
        public bool IsIntersect { get; set; }
        public bool IsActivity { get; set; }

        /// <summary>Referenced by a relationship but not part of the solution (stub box).</summary>
        public bool IsExternal { get; set; }

        public List<AttributeModel> Attributes { get; } = new List<AttributeModel>();
    }

    public class AttributeModel
    {
        public string LogicalName { get; set; }
        public string DisplayName { get; set; }

        /// <summary>Friendly type ("Text", "Choice", "Lookup(account)").</summary>
        public string TypeLabel { get; set; }

        public bool IsPrimaryId { get; set; }
        public bool IsPrimaryName { get; set; }
        public bool IsCustom { get; set; }
        public bool IsLookup { get; set; }
        public string RequiredLevel { get; set; }

        /// <summary>The column's description as written by its maker (may be null).</summary>
        public string Description { get; set; }

        /// <summary>Lookup target logical names (polymorphic lookups have several).</summary>
        public List<string> Targets { get; } = new List<string>();

        /// <summary>
        /// Allowed values of a choice, choices, status, status reason or yes/no
        /// column, in the order the maker defined them. Empty for other types.
        /// </summary>
        public List<OptionModel> Options { get; } = new List<OptionModel>();
    }

    /// <summary>One allowed value of a choice-style column.</summary>
    public class OptionModel
    {
        /// <summary>The stored number — what FetchXML, flows and the Web API use.</summary>
        public int Value { get; set; }

        public string Label { get; set; }

        /// <summary>
        /// For a status reason: the label of the status it belongs to (a status
        /// reason is only valid while the record is in that status). Else null.
        /// </summary>
        public string StateLabel { get; set; }
    }

    /// <summary>
    /// What an action on the parent ("one") record does to its child records.
    /// Values are the SDK's CascadeType names: Cascade, Active, UserOwned,
    /// NoCascade, RemoveLink, Restrict. Null when the metadata did not say.
    /// </summary>
    public class CascadeModel
    {
        public string Delete { get; set; }
        public string Assign { get; set; }
        public string Share { get; set; }
        public string Unshare { get; set; }
        public string Reparent { get; set; }
    }

    public enum RelationshipKind
    {
        OneToMany,
        ManyToMany
    }

    public class RelationshipModel
    {
        public string SchemaName { get; set; }
        public RelationshipKind Kind { get; set; }

        /// <summary>The "one" side of a 1:N (entity1 for N:N).</summary>
        public string ReferencedEntity { get; set; }

        /// <summary>The "many" side of a 1:N (entity2 for N:N).</summary>
        public string ReferencingEntity { get; set; }

        /// <summary>Logical name of the lookup column on the referencing table (1:N only).</summary>
        public string LookupAttribute { get; set; }

        /// <summary>Display name of the lookup column (1:N only).</summary>
        public string LookupDisplayName { get; set; }

        /// <summary>Intersect table logical name (N:N only).</summary>
        public string IntersectEntity { get; set; }

        public bool IsCustom { get; set; }

        /// <summary>Cascade behaviour (1:N only; null for N:N or when not retrieved).</summary>
        public CascadeModel Cascade { get; set; }

        public bool IsSelfReferential =>
            string.Equals(ReferencedEntity, ReferencingEntity, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Everything fetched for one solution: the input to the graph builder.</summary>
    public class ErdModel
    {
        public SolutionInfo Solution { get; set; }
        public List<EntityModel> Entities { get; } = new List<EntityModel>();
        public List<RelationshipModel> Relationships { get; } = new List<RelationshipModel>();
    }
}
