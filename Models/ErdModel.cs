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

        /// <summary>Lookup target logical names (polymorphic lookups have several).</summary>
        public List<string> Targets { get; } = new List<string>();
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
