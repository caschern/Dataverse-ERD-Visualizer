using System;
using System.Collections.Generic;
using System.Linq;
using DataverseErdVisualizer.Data;
using DataverseErdVisualizer.Models;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Xunit;

namespace DataverseErdVisualizer.Tests
{
    /// <summary>
    /// Drives the SDK-to-model mapping with real metadata objects. This is the
    /// only layer that otherwise gets no check until it runs against a live
    /// environment, and the parts it feeds — choice values, descriptions and
    /// cascade behaviour — fail silently by producing nothing.
    /// </summary>
    public class MetadataMappingTests
    {
        private static Label L(string text) => new Label(text, 1033);

        private static OptionMetadataCollection Options(params OptionMetadata[] options)
            => new OptionMetadataCollection(options.ToList());

        /// <summary>EntityMetadata.Attributes has no public setter.</summary>
        private static void SetAttributes(EntityMetadata em, params AttributeMetadata[] attributes)
            => typeof(EntityMetadata).GetProperty("Attributes")
                .GetSetMethod(nonPublic: true)
                .Invoke(em, new object[] { attributes });

        // ------------------------------------------------------- descriptions

        [Fact]
        public void Column_description_is_carried_across()
        {
            var am = new StringAttributeMetadata
            {
                LogicalName = "jn_casenumber",
                DisplayName = L("Case Number"),
                Description = L("Court-assigned identifier, unique per district.")
            };

            var mapped = MetadataRepository.MapAttribute(am, null);

            Assert.Equal("Case Number", mapped.DisplayName);
            Assert.Equal("Court-assigned identifier, unique per district.", mapped.Description);
        }

        [Fact]
        public void Missing_description_stays_null_rather_than_empty()
        {
            var mapped = MetadataRepository.MapAttribute(
                new StringAttributeMetadata { LogicalName = "jn_name" }, null);

            Assert.Null(mapped.Description);
        }

        // ------------------------------------------------------------ choices

        [Fact]
        public void Local_choice_values_are_captured_with_their_numbers()
        {
            var am = new PicklistAttributeMetadata
            {
                LogicalName = "jn_priority",
                DisplayName = L("Priority"),
                OptionSet = new OptionSetMetadata(Options(
                    new OptionMetadata(L("Low"), 100000000),
                    new OptionMetadata(L("High"), 100000001)))
            };

            var mapped = MetadataRepository.MapAttribute(am, null);

            Assert.Equal(2, mapped.Options.Count);
            Assert.Equal("Low", mapped.Options[0].Label);
            Assert.Equal(100000000, mapped.Options[0].Value);
            Assert.Equal(100000001, mapped.Options[1].Value);
        }

        [Fact]
        public void Yes_no_columns_capture_both_labels()
        {
            var am = new BooleanAttributeMetadata
            {
                LogicalName = "jn_sealed",
                DisplayName = L("Sealed"),
                OptionSet = new BooleanOptionSetMetadata(
                    new OptionMetadata(L("Sealed"), 1),
                    new OptionMetadata(L("Public"), 0))
            };

            var mapped = MetadataRepository.MapAttribute(am, null);

            Assert.Equal(2, mapped.Options.Count);
            Assert.Contains(mapped.Options, o => o.Label == "Sealed" && o.Value == 1);
            Assert.Contains(mapped.Options, o => o.Label == "Public" && o.Value == 0);
        }

        [Fact]
        public void Columns_without_choices_get_no_options()
        {
            var mapped = MetadataRepository.MapAttribute(
                new MoneyAttributeMetadata { LogicalName = "jn_amount" }, null);

            Assert.Empty(mapped.Options);
        }

        [Fact]
        public void Global_choice_without_options_is_queued_then_filled()
        {
            // A column bound to a global choice can come back naming the choice
            // but carrying no values; documenting it as empty would be a lie.
            var am = new PicklistAttributeMetadata
            {
                LogicalName = "jn_district",
                DisplayName = L("District"),
                OptionSet = new OptionSetMetadata { IsGlobal = true, Name = "jn_districts" }
            };

            var pending = new List<MetadataRepository.PendingOptionSet>();
            var mapped = MetadataRepository.MapAttribute(am, pending);

            Assert.Empty(mapped.Options);
            Assert.Single(pending);
            Assert.Equal("jn_districts", pending[0].Name);

            var globals = new OptionSetMetadataBase[]
            {
                new OptionSetMetadata(Options(
                    new OptionMetadata(L("North"), 1),
                    new OptionMetadata(L("South"), 2)))
                { Name = "jn_districts", IsGlobal = true }
            };
            MetadataRepository.ResolveGlobalOptionSets(pending, globals);

            Assert.Equal(2, mapped.Options.Count);
            Assert.Equal("North", mapped.Options[0].Label);
        }

        [Fact]
        public void Unresolvable_global_choice_is_left_empty_not_broken()
        {
            var am = new PicklistAttributeMetadata
            {
                LogicalName = "jn_district",
                OptionSet = new OptionSetMetadata { IsGlobal = true, Name = "jn_districts" }
            };
            var pending = new List<MetadataRepository.PendingOptionSet>();
            var mapped = MetadataRepository.MapAttribute(am, pending);

            MetadataRepository.ResolveGlobalOptionSets(pending, new OptionSetMetadataBase[0]);
            MetadataRepository.ResolveGlobalOptionSets(pending, null);

            Assert.Empty(mapped.Options);
        }

        // ------------------------------------------------------ status reasons

        [Fact]
        public void Status_reasons_record_the_status_they_belong_to()
        {
            var state = new StateAttributeMetadata
            {
                LogicalName = "statecode",
                DisplayName = L("Status"),
                OptionSet = new OptionSetMetadata(Options(
                    new OptionMetadata(L("Active"), 0),
                    new OptionMetadata(L("Inactive"), 1)))
            };
            var status = new StatusAttributeMetadata
            {
                LogicalName = "statuscode",
                DisplayName = L("Status Reason"),
                OptionSet = new OptionSetMetadata(Options(
                    new StatusOptionMetadata { Label = L("In Progress"), Value = 1, State = 0 },
                    new StatusOptionMetadata { Label = L("Resolved"), Value = 5, State = 1 }))
            };

            var em = new EntityMetadata { LogicalName = "jn_case" };
            SetAttributes(em, state, status);

            var entity = MetadataRepository.MapEntity(em, 0, new HashSet<Guid>(),
                new List<MetadataRepository.PendingOptionSet>());

            var mapped = entity.Attributes.Single(a => a.LogicalName == "statuscode");
            Assert.Equal("Active", mapped.Options.Single(o => o.Value == 1).StateLabel);
            Assert.Equal("Inactive", mapped.Options.Single(o => o.Value == 5).StateLabel);

            // A plain status column carries no parent status of its own.
            var mappedState = entity.Attributes.Single(a => a.LogicalName == "statecode");
            Assert.All(mappedState.Options, o => Assert.Null(o.StateLabel));
        }

        // ----------------------------------------------------------- cascades

        [Fact]
        public void Cascade_settings_are_carried_across()
        {
            var rel = new OneToManyRelationshipMetadata
            {
                SchemaName = "jn_contact_case",
                ReferencedEntity = "contact",
                ReferencingEntity = "jn_case",
                ReferencingAttribute = "jn_contactid",
                CascadeConfiguration = new CascadeConfiguration
                {
                    Delete = CascadeType.RemoveLink,
                    Assign = CascadeType.NoCascade,
                    Share = CascadeType.NoCascade,
                    Unshare = CascadeType.NoCascade,
                    Reparent = CascadeType.NoCascade
                }
            };

            var mapped = MetadataRepository.MapOneToMany(rel);

            Assert.NotNull(mapped.Cascade);
            Assert.Equal("RemoveLink", mapped.Cascade.Delete);
            Assert.Equal("NoCascade", mapped.Cascade.Assign);
        }

        [Fact]
        public void Relationship_without_cascade_metadata_maps_to_null()
        {
            var mapped = MetadataRepository.MapOneToMany(new OneToManyRelationshipMetadata
            {
                SchemaName = "jn_x",
                ReferencedEntity = "a",
                ReferencingEntity = "b"
            });

            Assert.Null(mapped.Cascade);
        }
    }
}
