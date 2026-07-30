using System;
using System.Collections.Generic;
using System.Linq;
using DataverseErdVisualizer.Models;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Metadata.Query;
using LogicalOperator = Microsoft.Xrm.Sdk.Query.LogicalOperator;
using SolutionInfo = DataverseErdVisualizer.Models.SolutionInfo;

namespace DataverseErdVisualizer.Data
{
    /// <summary>
    /// Retrieves table/column/relationship metadata for a solution in a single
    /// <c>RetrieveMetadataChanges</c> call (filtered by MetadataId, so even huge
    /// environments only send the solution's slice), then maps the SDK types to
    /// the SDK-free <see cref="ErdModel"/>.
    /// </summary>
    public static class MetadataRepository
    {
        public static ErdModel RetrieveModel(IOrganizationService service, SolutionInfo solution,
            Action<string> progress = null)
        {
            var model = new ErdModel { Solution = solution };

            progress?.Invoke("Reading solution components…");
            var components = SolutionRepository.RetrieveEntityComponents(service, solution.Id);
            if (components.EntityIds.Count == 0) return model;

            progress?.Invoke($"Retrieving metadata for {components.EntityIds.Count} tables…");
            var entities = RetrieveByMetadataId(service, components.EntityIds);

            var knownLogicalNames = new HashSet<string>(
                entities.Select(e => e.LogicalName), StringComparer.OrdinalIgnoreCase);

            foreach (var em in entities)
            {
                int behavior = 0;
                if (em.MetadataId != null)
                    components.RootBehavior.TryGetValue(em.MetadataId.Value, out behavior);

                model.Entities.Add(MapEntity(em, behavior, components.AttributeIds));
                CollectRelationships(em, model.Relationships);
            }

            // Display names for lookup columns power the edge labels.
            ResolveLookupDisplayNames(model);

            // Stub metadata for tables referenced by a relationship but outside
            // the solution, so they can render as grey external boxes.
            var externals = CollectExternalNames(model, knownLogicalNames);
            if (externals.Count > 0)
            {
                progress?.Invoke($"Resolving {externals.Count} related external tables…");
                foreach (var em in RetrieveStubsByLogicalName(service, externals))
                {
                    if (em.IsIntersect == true) continue;
                    var stub = MapEntityShell(em);
                    stub.IsExternal = true;
                    model.Entities.Add(stub);
                }
            }

            return model;
        }

        // ------------------------------------------------------ metadata calls

        private static IList<EntityMetadata> RetrieveByMetadataId(
            IOrganizationService service, List<Guid> ids)
        {
            var query = new EntityQueryExpression
            {
                Criteria = new MetadataFilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new MetadataConditionExpression("MetadataId",
                            MetadataConditionOperator.In, ids.ToArray())
                    }
                },
                Properties = new MetadataPropertiesExpression(
                    "LogicalName", "SchemaName", "DisplayName", "Description",
                    "OwnershipType", "IsCustomEntity", "IsIntersect", "IsActivity",
                    "PrimaryIdAttribute", "PrimaryNameAttribute",
                    "Attributes", "OneToManyRelationships", "ManyToOneRelationships",
                    "ManyToManyRelationships"),
                AttributeQuery = new AttributeQueryExpression
                {
                    Properties = new MetadataPropertiesExpression(
                        "LogicalName", "SchemaName", "DisplayName", "AttributeType",
                        "AttributeTypeName", "IsPrimaryId", "IsPrimaryName",
                        "IsCustomAttribute", "RequiredLevel", "Targets", "AttributeOf")
                },
                RelationshipQuery = new RelationshipQueryExpression
                {
                    Properties = new MetadataPropertiesExpression(
                        "SchemaName", "ReferencedEntity", "ReferencingEntity",
                        "ReferencedAttribute", "ReferencingAttribute",
                        "Entity1LogicalName", "Entity2LogicalName", "IntersectEntityName",
                        "IsCustomRelationship")
                }
            };

            return Execute(service, query);
        }

        private static IList<EntityMetadata> RetrieveStubsByLogicalName(
            IOrganizationService service, List<string> logicalNames)
        {
            var query = new EntityQueryExpression
            {
                Criteria = new MetadataFilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new MetadataConditionExpression("LogicalName",
                            MetadataConditionOperator.In, logicalNames.ToArray())
                    }
                },
                Properties = new MetadataPropertiesExpression(
                    "LogicalName", "SchemaName", "DisplayName", "Description",
                    "OwnershipType", "IsCustomEntity", "IsIntersect", "IsActivity",
                    "PrimaryIdAttribute", "PrimaryNameAttribute")
            };

            return Execute(service, query);
        }

        private static IList<EntityMetadata> Execute(IOrganizationService service, EntityQueryExpression query)
        {
            var request = new RetrieveMetadataChangesRequest { Query = query };
            var response = (RetrieveMetadataChangesResponse)service.Execute(request);
            return response.EntityMetadata;
        }

        // ------------------------------------------------------------- mapping

        private static EntityModel MapEntityShell(EntityMetadata em)
        {
            return new EntityModel
            {
                LogicalName = em.LogicalName,
                SchemaName = em.SchemaName,
                DisplayName = Label(em.DisplayName) ?? em.LogicalName,
                Description = Label(em.Description),
                PrimaryIdAttribute = em.PrimaryIdAttribute,
                PrimaryNameAttribute = em.PrimaryNameAttribute,
                OwnershipType = em.OwnershipType?.ToString(),
                IsCustom = em.IsCustomEntity ?? false,
                IsIntersect = em.IsIntersect ?? false,
                IsActivity = em.IsActivity ?? false
            };
        }

        private static EntityModel MapEntity(EntityMetadata em, int rootBehavior, HashSet<Guid> attributeComponents)
        {
            var entity = MapEntityShell(em);
            if (em.Attributes == null) return entity;

            // Segmented solutions ("do not include subcomponents"): only columns
            // explicitly added to the solution — plus the identity columns.
            bool segmented = rootBehavior != 0;

            foreach (var am in em.Attributes)
            {
                if (!IsRealDataColumn(am)) continue;

                bool identity = (am.IsPrimaryId ?? false) || (am.IsPrimaryName ?? false);
                if (segmented && !identity &&
                    (am.MetadataId == null || !attributeComponents.Contains(am.MetadataId.Value)))
                    continue;

                entity.Attributes.Add(MapAttribute(am));
            }
            return entity;
        }

        /// <summary>Filters out virtual companion columns and non-data plumbing.</summary>
        private static bool IsRealDataColumn(AttributeMetadata am)
        {
            if (am.AttributeOf != null) return false;    // _name / _yominame companions

            switch (am.AttributeType)
            {
                case AttributeTypeCode.Virtual:
                    // Multi-select choice, file and image columns report Virtual.
                    var typeName = am.AttributeTypeName?.Value;
                    return typeName == "MultiSelectPicklistType" ||
                           typeName == "FileType" ||
                           typeName == "ImageType";
                case AttributeTypeCode.EntityName:
                case AttributeTypeCode.ManagedProperty:
                case AttributeTypeCode.CalendarRules:
                case AttributeTypeCode.PartyList:
                    return false;
                case AttributeTypeCode.Uniqueidentifier:
                    return am.IsPrimaryId ?? false;      // only the PK, not e.g. address ids
                default:
                    return true;
            }
        }

        private static AttributeModel MapAttribute(AttributeMetadata am)
        {
            var attr = new AttributeModel
            {
                LogicalName = am.LogicalName,
                DisplayName = Label(am.DisplayName) ?? am.LogicalName,
                IsPrimaryId = am.IsPrimaryId ?? false,
                IsPrimaryName = am.IsPrimaryName ?? false,
                IsCustom = am.IsCustomAttribute ?? false,
                RequiredLevel = am.RequiredLevel?.Value.ToString()
            };

            if (am is LookupAttributeMetadata lookup && lookup.Targets != null)
                attr.Targets.AddRange(lookup.Targets);

            attr.IsLookup = am.AttributeType == AttributeTypeCode.Lookup ||
                            am.AttributeType == AttributeTypeCode.Customer ||
                            am.AttributeType == AttributeTypeCode.Owner;

            attr.TypeLabel = TypeLabel(am, attr);
            return attr;
        }

        private static string TypeLabel(AttributeMetadata am, AttributeModel attr)
        {
            switch (am.AttributeType)
            {
                case AttributeTypeCode.String: return "Text";
                case AttributeTypeCode.Memo: return "Multiline";
                case AttributeTypeCode.Integer: return "Whole Num";
                case AttributeTypeCode.BigInt: return "Big Int";
                case AttributeTypeCode.Decimal: return "Decimal";
                case AttributeTypeCode.Double: return "Float";
                case AttributeTypeCode.Money: return "Currency";
                case AttributeTypeCode.Boolean: return "Yes/No";
                case AttributeTypeCode.DateTime: return "Date/Time";
                case AttributeTypeCode.Picklist: return "Choice";
                case AttributeTypeCode.State: return "Status";
                case AttributeTypeCode.Status: return "Status Reason";
                case AttributeTypeCode.Uniqueidentifier: return "GUID";
                case AttributeTypeCode.Customer: return "Customer";
                case AttributeTypeCode.Owner: return "Owner";
                case AttributeTypeCode.Lookup:
                    if (attr.Targets.Count == 0) return "Lookup";
                    if (attr.Targets.Count == 1) return "Lookup(" + attr.Targets[0] + ")";
                    return "Lookup(" + attr.Targets[0] + " +" + (attr.Targets.Count - 1) + ")";
                case AttributeTypeCode.Virtual:
                    var typeName = am.AttributeTypeName?.Value;
                    if (typeName == "MultiSelectPicklistType") return "Choices";
                    if (typeName == "FileType") return "File";
                    if (typeName == "ImageType") return "Image";
                    return "Virtual";
                default:
                    return am.AttributeType?.ToString() ?? "";
            }
        }

        private static void CollectRelationships(EntityMetadata em, List<RelationshipModel> into)
        {
            if (em.OneToManyRelationships != null)
                foreach (var rel in em.OneToManyRelationships)
                    into.Add(MapOneToMany(rel));

            if (em.ManyToOneRelationships != null)
                foreach (var rel in em.ManyToOneRelationships)
                    into.Add(MapOneToMany(rel));

            if (em.ManyToManyRelationships != null)
                foreach (var rel in em.ManyToManyRelationships)
                    into.Add(new RelationshipModel
                    {
                        SchemaName = rel.SchemaName,
                        Kind = RelationshipKind.ManyToMany,
                        ReferencedEntity = rel.Entity1LogicalName,
                        ReferencingEntity = rel.Entity2LogicalName,
                        IntersectEntity = rel.IntersectEntityName,
                        IsCustom = rel.IsCustomRelationship ?? false
                    });
        }

        private static RelationshipModel MapOneToMany(OneToManyRelationshipMetadata rel)
        {
            return new RelationshipModel
            {
                SchemaName = rel.SchemaName,
                Kind = RelationshipKind.OneToMany,
                ReferencedEntity = rel.ReferencedEntity,
                ReferencingEntity = rel.ReferencingEntity,
                LookupAttribute = rel.ReferencingAttribute,
                IsCustom = rel.IsCustomRelationship ?? false
            };
        }

        /// <summary>Edge labels use the lookup column's display name when we have it.</summary>
        private static void ResolveLookupDisplayNames(ErdModel model)
        {
            var byEntity = model.Entities
                .GroupBy(e => e.LogicalName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var rel in model.Relationships)
            {
                if (rel.Kind != RelationshipKind.OneToMany || rel.LookupAttribute == null) continue;
                if (!byEntity.TryGetValue(rel.ReferencingEntity, out var entity)) continue;
                var attr = entity.Attributes.FirstOrDefault(a =>
                    string.Equals(a.LogicalName, rel.LookupAttribute, StringComparison.OrdinalIgnoreCase));
                rel.LookupDisplayName = attr?.DisplayName ?? rel.LookupAttribute;
            }
        }

        private static List<string> CollectExternalNames(ErdModel model, HashSet<string> known)
        {
            var externals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rel in model.Relationships)
            {
                if (rel.ReferencedEntity != null && !known.Contains(rel.ReferencedEntity))
                    externals.Add(rel.ReferencedEntity);
                if (rel.ReferencingEntity != null && !known.Contains(rel.ReferencingEntity))
                    externals.Add(rel.ReferencingEntity);
            }
            return externals.ToList();
        }

        private static string Label(Label label)
            => label?.UserLocalizedLabel?.Label
               ?? label?.LocalizedLabels?.FirstOrDefault()?.Label;
    }
}
