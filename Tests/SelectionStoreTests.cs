using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DataverseErdVisualizer;
using Xunit;

namespace DataverseErdVisualizer.Tests
{
    /// <summary>
    /// Each test uses its own random solution key, so it can never touch the
    /// saved selection of a real solution.
    /// </summary>
    public class SelectionStoreTests : IDisposable
    {
        private readonly string _key = "test-solution-" + Guid.NewGuid().ToString("N");

        public void Dispose() => SelectionStore.Delete(_key);

        private static Dictionary<string, bool> Ticks(params (string table, bool ticked)[] entries)
            => entries.ToDictionary(e => e.table, e => e.ticked, StringComparer.OrdinalIgnoreCase);

        [Fact]
        public void Ticked_and_unticked_tables_survive_a_save_and_load()
        {
            SelectionStore.Save(_key, Ticks(("jn_case", true), ("contact", false), ("jn_party", true)));

            var loaded = SelectionStore.Load(_key);

            Assert.Equal(3, loaded.Count);
            Assert.True(loaded["jn_case"]);
            Assert.False(loaded["contact"]);
            Assert.True(loaded["JN_PARTY"]);   // logical names compare case-insensitively
        }

        [Fact]
        public void A_deliberately_unticked_table_stays_unticked_in_a_small_solution()
        {
            // Small solutions tick everything by default. Storing only the
            // ticked tables would make this one indistinguishable from new.
            var saved = Ticks(("account", true), ("contact", false));

            var resolved = SelectionStore.Resolve(new[] { "account", "contact" }, saved, newTableDefault: true);

            Assert.False(resolved["contact"]);
        }

        [Fact]
        public void An_unticked_table_survives_the_file_not_just_the_merge()
        {
            // End to end: if the file recorded only ticked tables, "contact"
            // would come back as unknown and be re-ticked as if it were new.
            SelectionStore.Save(_key, Ticks(("account", true), ("contact", false)));

            var resolved = SelectionStore.Resolve(new[] { "account", "contact" },
                SelectionStore.Load(_key), newTableDefault: true);

            Assert.True(resolved["account"]);
            Assert.False(resolved["contact"]);
        }

        [Fact]
        public void A_table_new_to_the_solution_gets_the_default_for_its_size()
        {
            var saved = Ticks(("account", true), ("contact", false));
            var current = new[] { "account", "contact", "jn_newtable" };

            Assert.True(SelectionStore.Resolve(current, saved, newTableDefault: true)["jn_newtable"]);
            Assert.False(SelectionStore.Resolve(current, saved, newTableDefault: false)["jn_newtable"]);
        }

        [Fact]
        public void Tables_that_have_left_the_solution_are_dropped()
        {
            var saved = Ticks(("account", true), ("jn_retired", true));

            var resolved = SelectionStore.Resolve(new[] { "account" }, saved, newTableDefault: false);

            Assert.Equal(new[] { "account" }, resolved.Keys.ToArray());
        }

        [Fact]
        public void With_nothing_saved_every_table_gets_the_default()
        {
            var resolved = SelectionStore.Resolve(new[] { "a", "b", "c" },
                new Dictionary<string, bool>(), newTableDefault: true);

            Assert.All(resolved.Values, Assert.True);
            Assert.Equal(3, resolved.Count);
        }

        [Fact]
        public void Each_solution_keeps_its_own_selection()
        {
            var other = "test-solution-" + Guid.NewGuid().ToString("N");
            try
            {
                SelectionStore.Save(_key, Ticks(("contact", true)));
                SelectionStore.Save(other, Ticks(("contact", false)));

                Assert.True(SelectionStore.Load(_key)["contact"]);
                Assert.False(SelectionStore.Load(other)["contact"]);
            }
            finally
            {
                SelectionStore.Delete(other);
            }
        }

        [Fact]
        public void An_unknown_solution_has_no_saved_selection()
        {
            Assert.Empty(SelectionStore.Load(_key));
            Assert.Empty(SelectionStore.Load(null));
            Assert.Empty(SelectionStore.Load("   "));
        }

        [Fact]
        public void A_damaged_file_yields_only_the_readable_lines_and_does_not_throw()
        {
            SelectionStore.Save(_key, Ticks(("account", true)));
            var path = Directory.GetFiles(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MscrmTools", "XrmToolBox", "Settings", "DataverseErdVisualizer", "selections"),
                _key + ".selection").Single();

            File.WriteAllText(path, "+account\n\ngarbage line\n+\n-contact\n?what\n");

            var loaded = SelectionStore.Load(_key);

            Assert.Equal(2, loaded.Count);
            Assert.True(loaded["account"]);
            Assert.False(loaded["contact"]);
        }
    }
}
