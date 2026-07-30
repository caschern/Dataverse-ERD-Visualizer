using System;
using System.Collections.Generic;
using DataverseErdVisualizer.Models;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SolutionInfo = DataverseErdVisualizer.Models.SolutionInfo;

namespace DataverseErdVisualizer.Data
{
    /// <summary>The entity/attribute component ids of one solution.</summary>
    public class SolutionEntityComponents
    {
        /// <summary>MetadataIds of the solution's tables (componenttype 1).</summary>
        public List<Guid> EntityIds { get; } = new List<Guid>();

        /// <summary>
        /// rootcomponentbehavior per table: 0 = include subcomponents,
        /// 1 = do not include, 2 = include shell only. Missing = 0.
        /// </summary>
        public Dictionary<Guid, int> RootBehavior { get; } = new Dictionary<Guid, int>();

        /// <summary>MetadataIds of individually added columns (componenttype 2).</summary>
        public HashSet<Guid> AttributeIds { get; } = new HashSet<Guid>();
    }

    /// <summary>Reads the <c>solution</c> and <c>solutioncomponent</c> tables.</summary>
    public static class SolutionRepository
    {
        private const int ComponentTypeEntity = 1;
        private const int ComponentTypeAttribute = 2;

        public static List<SolutionInfo> RetrieveSolutions(IOrganizationService service)
        {
            var query = new QueryExpression("solution")
            {
                ColumnSet = new ColumnSet("solutionid", "uniquename", "friendlyname",
                    "version", "ismanaged", "publisherid"),
                Criteria = new FilterExpression(LogicalOperator.And),
                Orders = { new OrderExpression("friendlyname", OrderType.Ascending) },
                PageInfo = new PagingInfo { Count = 500, PageNumber = 1 }
            };
            query.Criteria.AddCondition("isvisible", ConditionOperator.Equal, true);

            var results = new List<SolutionInfo>();
            while (true)
            {
                var page = service.RetrieveMultiple(query);
                foreach (var e in page.Entities)
                {
                    results.Add(new SolutionInfo
                    {
                        Id = e.Id,
                        UniqueName = e.GetAttributeValue<string>("uniquename"),
                        FriendlyName = e.GetAttributeValue<string>("friendlyname"),
                        Version = e.GetAttributeValue<string>("version"),
                        IsManaged = e.GetAttributeValue<bool?>("ismanaged") ?? false,
                        Publisher = e.FormattedValues.Contains("publisherid")
                            ? e.FormattedValues["publisherid"]
                            : e.GetAttributeValue<EntityReference>("publisherid")?.Name
                    });
                }
                if (!page.MoreRecords) break;
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
            return results;
        }

        public static SolutionEntityComponents RetrieveEntityComponents(
            IOrganizationService service, Guid solutionId)
        {
            var query = new QueryExpression("solutioncomponent")
            {
                ColumnSet = new ColumnSet("objectid", "componenttype", "rootcomponentbehavior"),
                Criteria = new FilterExpression(LogicalOperator.And),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            query.Criteria.AddCondition("solutionid", ConditionOperator.Equal, solutionId);
            query.Criteria.AddCondition("componenttype", ConditionOperator.In,
                ComponentTypeEntity, ComponentTypeAttribute);

            var result = new SolutionEntityComponents();
            while (true)
            {
                var page = service.RetrieveMultiple(query);
                foreach (var e in page.Entities)
                {
                    var objectId = e.GetAttributeValue<Guid?>("objectid");
                    if (objectId == null) continue;
                    var type = e.GetAttributeValue<OptionSetValue>("componenttype")?.Value ?? -1;

                    if (type == ComponentTypeEntity)
                    {
                        result.EntityIds.Add(objectId.Value);
                        var behavior = e.GetAttributeValue<OptionSetValue>("rootcomponentbehavior")?.Value ?? 0;
                        result.RootBehavior[objectId.Value] = behavior;
                    }
                    else if (type == ComponentTypeAttribute)
                    {
                        result.AttributeIds.Add(objectId.Value);
                    }
                }
                if (!page.MoreRecords) break;
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
            return result;
        }
    }
}
