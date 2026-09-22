using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace DataverseErdVisualizer
{
    /// <summary>
    /// Remembers the display options (column mode, toggles) between sessions.
    ///
    /// Every public read/write option that is a yes/no, a number or a choice
    /// is stored, found by reflection rather than listed by hand: a new option
    /// is then remembered the day it is added, instead of waiting for someone to
    /// notice it resets every session. Per-diagram state such as which tables
    /// are ticked is not an option and is never stored here.
    ///
    /// Plain "Name=Value" lines rather than JSON, for the same reason as the
    /// layout store: the host supplies its own older Newtonsoft at runtime.
    /// Losing this file is cosmetic, so reading and writing never throw; a
    /// damaged or unrecognised value simply leaves that option at its default.
    /// </summary>
    public static class OptionsStore
    {
        public static string DefaultPath => Path.Combine(SettingsFolder.Root, "options.txt");

        /// <summary>Applies saved options onto <paramref name="options"/>.</summary>
        public static void Load(ErdOptions options, string path = null)
        {
            if (options == null) return;
            try
            {
                path = path ?? DefaultPath;
                if (!File.Exists(path)) return;

                var persistable = Persistable();
                foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    PropertyInfo property;
                    if (!persistable.TryGetValue(line.Substring(0, eq).Trim(), out property)) continue;

                    object value;
                    if (TryParse(property.PropertyType, line.Substring(eq + 1).Trim(), out value))
                        property.SetValue(options, value);
                }
            }
            catch
            {
                // Whatever was applied before the failure stands; the rest keep
                // their defaults.
            }
        }

        public static void Save(ErdOptions options, string path = null)
        {
            if (options == null) return;
            try
            {
                path = path ?? DefaultPath;
                var folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

                var sb = new StringBuilder();
                sb.AppendLine("# Dataverse ERD Visualizer display options");
                foreach (var property in Persistable().Values.OrderBy(p => p.Name, StringComparer.Ordinal))
                    sb.Append(property.Name).Append('=').AppendLine(Format(property.GetValue(options)));

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Not being able to remember options must not break the tool.
            }
        }

        /// <summary>The option names that are stored (for tests and diagnostics).</summary>
        public static IEnumerable<string> PersistedOptionNames => Persistable().Keys;

        private static Dictionary<string, PropertyInfo> Persistable()
            => typeof(ErdOptions)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
                .Where(p => p.PropertyType == typeof(bool) ||
                            p.PropertyType == typeof(int) ||
                            p.PropertyType.IsEnum)
                .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

        private static bool TryParse(Type type, string text, out object value)
        {
            value = null;

            if (type == typeof(bool))
            {
                bool b;
                if (!bool.TryParse(text, out b)) return false;
                value = b;
                return true;
            }

            if (type == typeof(int))
            {
                int i;
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) return false;
                value = i;
                return true;
            }

            if (type.IsEnum)
            {
                // By name only: a bare number would be accepted by Enum.Parse
                // even when it names no member.
                var match = Enum.GetNames(type)
                    .FirstOrDefault(n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase));
                if (match == null) return false;
                value = Enum.Parse(type, match);
                return true;
            }

            return false;
        }

        private static string Format(object value)
        {
            if (value is int) return ((int)value).ToString(CultureInfo.InvariantCulture);
            return value?.ToString() ?? "";
        }
    }
}
