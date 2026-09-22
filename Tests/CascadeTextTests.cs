using DataverseErdVisualizer.Exporters;
using DataverseErdVisualizer.Models;
using Xunit;

namespace DataverseErdVisualizer.Tests
{
    public class CascadeTextTests
    {
        private static CascadeModel All(string value) => new CascadeModel
        {
            Delete = value,
            Assign = value,
            Share = value,
            Unshare = value,
            Reparent = value
        };

        private static CascadeModel Referential(string delete) => new CascadeModel
        {
            Delete = delete,
            Assign = "NoCascade",
            Share = "NoCascade",
            Unshare = "NoCascade",
            Reparent = "NoCascade"
        };

        [Theory]
        [InlineData("Cascade", "Parental")]
        public void Recognises_parental(string value, string expected)
            => Assert.Equal(expected, CascadeText.Name(All(value)));

        [Fact]
        public void Recognises_the_referential_behaviours()
        {
            Assert.Equal("Referential", CascadeText.Name(Referential("RemoveLink")));
            Assert.Equal("Referential, restrict delete", CascadeText.Name(Referential("Restrict")));
        }

        [Fact]
        public void Anything_else_is_configurable_cascading()
        {
            var mixed = Referential("RemoveLink");
            mixed.Assign = "Cascade";

            Assert.Equal("Configurable cascading", CascadeText.Name(mixed));
        }

        [Fact]
        public void Null_cascade_produces_nothing()
        {
            Assert.Null(CascadeText.Name(null));
            Assert.Null(CascadeText.Describe(null, "Contact", "Case", "Assigned Judge"));
        }

        [Fact]
        public void Parental_says_the_children_are_deleted_too()
        {
            var text = CascadeText.Describe(All("Cascade"), "Contact", "Case", "Assigned Judge");

            Assert.Contains("Deleting Contact records also deletes their related Case records.", text);
            Assert.Contains("Assign, share, unshare and reparent cascade to all related Case records.", text);
        }

        [Fact]
        public void Referential_says_the_children_survive()
        {
            var text = CascadeText.Describe(Referential("RemoveLink"), "Contact", "Case", "Assigned Judge");

            Assert.Contains(
                "Deleting Contact records clears the Assigned Judge lookup on related Case records, which are kept.",
                text);
            Assert.Contains("do not cascade to related Case records", text);
        }

        [Fact]
        public void Restrict_delete_says_the_parent_is_blocked()
        {
            var text = CascadeText.Describe(Referential("Restrict"), "Contact", "Case", "Assigned Judge");

            Assert.Contains("Contact records cannot be deleted while related Case records still reference them.",
                text);
        }

        [Fact]
        public void Mixed_settings_are_grouped_rather_than_listed_one_by_one()
        {
            var mixed = new CascadeModel
            {
                Delete = "Cascade",
                Assign = "Cascade",
                Share = "Cascade",
                Unshare = "NoCascade",
                Reparent = "NoCascade"
            };

            var text = CascadeText.Describe(mixed, "Contact", "Case", "Assigned Judge");

            Assert.Contains("Assign and share cascade to all related Case records", text);
            Assert.Contains("unshare and reparent do not cascade to related Case records", text);
        }

        [Fact]
        public void Partial_metadata_still_produces_what_it_can()
        {
            // Only the delete behaviour came back.
            var text = CascadeText.Describe(new CascadeModel { Delete = "Cascade" },
                "Contact", "Case", "Assigned Judge");

            Assert.Equal("Deleting Contact records also deletes their related Case records.", text);
        }

        [Fact]
        public void Remove_link_without_a_lookup_name_still_reads()
        {
            var text = CascadeText.Describe(Referential("RemoveLink"), "Contact", "Case", null);

            Assert.Contains("clears the lookup on related Case records", text);
        }
    }
}
