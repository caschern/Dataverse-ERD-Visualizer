using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DataverseErdVisualizer;
using Xunit;

namespace DataverseErdVisualizer.Tests
{
    /// <summary>
    /// Every test works on a temporary file. The real options file lives in
    /// the user's XrmToolBox settings, and running the suite must never
    /// overwrite someone's preferences.
    /// </summary>
    public class OptionsStoreTests : IDisposable
    {
        private readonly string _path =
            Path.Combine(Path.GetTempPath(), "erd-options-" + Guid.NewGuid().ToString("N") + ".txt");

        public void Dispose()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }

        private static IEnumerable<PropertyInfo> Persisted()
            => typeof(ErdOptions).GetProperties()
                .Where(p => OptionsStore.PersistedOptionNames.Contains(p.Name));

        /// <summary>An options object with every stored value moved off its default.</summary>
        private static ErdOptions AllChanged()
        {
            var options = new ErdOptions();
            foreach (var p in Persisted())
            {
                var current = p.GetValue(options);
                if (p.PropertyType == typeof(bool)) p.SetValue(options, !(bool)current);
                else if (p.PropertyType == typeof(int)) p.SetValue(options, (int)current + 7);
                else if (p.PropertyType.IsEnum)
                {
                    var other = Enum.GetValues(p.PropertyType).Cast<object>()
                        .First(v => !v.Equals(current));
                    p.SetValue(options, other);
                }
            }
            return options;
        }

        [Fact]
        public void Every_option_survives_a_save_and_load()
        {
            var saved = AllChanged();
            OptionsStore.Save(saved, _path);

            var loaded = new ErdOptions();
            OptionsStore.Load(loaded, _path);

            foreach (var p in Persisted())
                Assert.True(Equals(p.GetValue(saved), p.GetValue(loaded)),
                    $"{p.Name}: saved {p.GetValue(saved)}, loaded {p.GetValue(loaded)}");
        }

        [Fact]
        public void Every_simple_option_is_remembered_including_ones_added_later()
        {
            // Found by reflection, so an option is remembered the day it is
            // added rather than resetting every session until someone notices.
            var simple = typeof(ErdOptions).GetProperties()
                .Where(p => p.CanWrite &&
                            (p.PropertyType == typeof(bool) || p.PropertyType == typeof(int) ||
                             p.PropertyType.IsEnum))
                .Select(p => p.Name)
                .OrderBy(n => n)
                .ToList();

            Assert.Equal(simple, OptionsStore.PersistedOptionNames.OrderBy(n => n).ToList());
            Assert.Contains("WrapWideRanks", simple);
            Assert.Contains("ClusterSatelliteTables", simple);
            Assert.Contains("AttributeMode", simple);
        }

        [Fact]
        public void Ticked_tables_are_not_an_option_and_are_never_stored()
        {
            var options = new ErdOptions { SelectedEntities = new HashSet<string> { "jn_case" } };
            OptionsStore.Save(options, _path);

            Assert.DoesNotContain("SelectedEntities", File.ReadAllText(_path));
            Assert.DoesNotContain("jn_case", File.ReadAllText(_path));
        }

        [Fact]
        public void No_saved_file_leaves_the_defaults()
        {
            var options = new ErdOptions();
            OptionsStore.Load(options, _path);   // does not exist

            AssertDefaults(options);
        }

        [Fact]
        public void A_damaged_file_leaves_the_defaults_and_does_not_throw()
        {
            File.WriteAllText(_path, "\u0000\u0001 not an options file\n====\n=True\n");

            var options = new ErdOptions();
            OptionsStore.Load(options, _path);

            AssertDefaults(options);
        }

        [Fact]
        public void Bad_values_are_skipped_while_good_ones_apply()
        {
            File.WriteAllText(_path,
                "# a file from an older or newer version\n" +
                "ShowEdgeLabels=False\n" +          // good
                "WrapWideRanks=maybe\n" +           // bad bool
                "MaxAttributesPerEntity=lots\n" +   // bad int
                "AttributeMode=999\n" +             // a number naming no member
                "SomeFutureOption=True\n");         // unknown key

            var options = new ErdOptions();
            OptionsStore.Load(options, _path);

            Assert.False(options.ShowEdgeLabels);
            Assert.True(options.WrapWideRanks);
            Assert.Equal(new ErdOptions().MaxAttributesPerEntity, options.MaxAttributesPerEntity);
            Assert.Equal(AttributeDisplayMode.KeysAndLookups, options.AttributeMode);
        }

        [Fact]
        public void Names_and_values_are_read_regardless_of_case()
        {
            File.WriteAllText(_path, "attributemode=all\nshowedgelabels=FALSE\n");

            var options = new ErdOptions();
            OptionsStore.Load(options, _path);

            Assert.Equal(AttributeDisplayMode.All, options.AttributeMode);
            Assert.False(options.ShowEdgeLabels);
        }

        [Fact]
        public void The_real_file_lives_with_the_other_XrmToolBox_settings()
        {
            Assert.Contains(Path.Combine("MscrmTools", "XrmToolBox", "Settings", "DataverseErdVisualizer"),
                OptionsStore.DefaultPath);
        }

        private static void AssertDefaults(ErdOptions options)
        {
            var defaults = new ErdOptions();
            foreach (var p in Persisted())
                Assert.True(Equals(p.GetValue(defaults), p.GetValue(options)), $"{p.Name} changed");
        }
    }
}
