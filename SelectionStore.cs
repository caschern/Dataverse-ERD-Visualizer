using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DataverseErdVisualizer
{
    /// <summary>
    /// Remembers which tables are ticked, one file per solution.
    ///
    /// Every table known at save time is written as ticked (+) or unticked
    /// (-), not just the ticked ones. That is what lets a table ADDED to the
    /// solution since be told apart from one the user deliberately unticked:
    /// the new one gets the normal default for the solution's size, the
    /// unticked one stays unticked.
    ///
    /// Plain text, never throws: a lost or damaged file just means the
    /// defaults, as on first use.
    /// </summary>
    public static class SelectionStore
    {
        public static Dictionary<string, bool> Load(string solutionKey)
        {
            var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var path = PathFor(solutionKey);
                if (path == null || !File.Exists(path)) return result;

                foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    var line = raw.Trim();
                    if (line.Length < 2) continue;

                    var name = line.Substring(1).Trim();
                    if (name.Length == 0) continue;

                    if (line[0] == '+') result[name] = true;
                    else if (line[0] == '-') result[name] = false;
                }
            }
            catch
            {
                result.Clear();
            }
            return result;
        }

        public static void Save(string solutionKey, IDictionary<string, bool> ticks)
        {
            try
            {
                var path = PathFor(solutionKey);
                if (path == null || ticks == null) return;

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var sb = new StringBuilder();
                foreach (var entry in ticks.OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase))
                    sb.Append(entry.Value ? '+' : '-').AppendLine(entry.Key);

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Not being able to remember the selection must not break the tool.
            }
        }

        public static void Delete(string solutionKey)
        {
            try
            {
                var path = PathFor(solutionKey);
                if (path != null && File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        /// <summary>
        /// The tick for each of the solution's current tables: what it was last
        /// time if the table was known then, otherwise <paramref name="newTableDefault"/>.
        /// Tables that have since left the solution are dropped.
        /// </summary>
        public static Dictionary<string, bool> Resolve(IEnumerable<string> tables,
            IDictionary<string, bool> saved, bool newTableDefault)
        {
            var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var table in tables ?? Enumerable.Empty<string>())
            {
                bool ticked;
                result[table] = saved != null && saved.TryGetValue(table, out ticked)
                    ? ticked
                    : newTableDefault;
            }
            return result;
        }

        private static string PathFor(string solutionKey)
            => SettingsFolder.PerSolutionFile("selections", solutionKey, ".selection");
    }
}
