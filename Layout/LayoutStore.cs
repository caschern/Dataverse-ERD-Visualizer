using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace DataverseErdVisualizer.Layout
{
    /// <summary>
    /// Remembers hand-placed table positions between sessions, one file per
    /// solution.
    ///
    /// The format is deliberately a plain tab-separated list rather than JSON:
    /// the XrmToolBox host supplies its own (older) Newtonsoft at runtime, and
    /// this codebase has already been bitten once by compiling against a newer
    /// one. Nothing here is worth a dependency.
    ///
    /// Losing a layout file is a cosmetic annoyance, never a failure, so every
    /// operation swallows its errors and carries on.
    /// </summary>
    public static class LayoutStore
    {
        private static string Folder => Path.Combine(SettingsFolder.Root, "layouts");

        public static Dictionary<string, PointF> Load(string solutionKey)
        {
            var result = new Dictionary<string, PointF>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var path = PathFor(solutionKey);
                if (path == null || !File.Exists(path)) return result;

                foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    var parts = line.Split('\t');
                    if (parts.Length != 3) continue;

                    float x, y;
                    if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out x)) continue;
                    if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out y)) continue;
                    if (parts[0].Length == 0) continue;

                    result[parts[0]] = new PointF(x, y);
                }
            }
            catch
            {
                // A damaged or unreadable layout file just means no pins.
                result.Clear();
            }
            return result;
        }

        public static void Save(string solutionKey, IDictionary<string, PointF> positions)
        {
            try
            {
                var path = PathFor(solutionKey);
                if (path == null) return;

                if (positions == null || positions.Count == 0)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }

                Directory.CreateDirectory(Folder);
                var sb = new StringBuilder();
                foreach (var entry in positions)
                    sb.Append(entry.Key).Append('\t')
                      .Append(entry.Value.X.ToString("0.##", CultureInfo.InvariantCulture)).Append('\t')
                      .Append(entry.Value.Y.ToString("0.##", CultureInfo.InvariantCulture)).AppendLine();

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Not being able to remember the layout must not break the tool.
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

        private static string PathFor(string solutionKey)
        {
            if (string.IsNullOrWhiteSpace(solutionKey)) return null;

            var name = solutionKey.Trim();
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (name.Length > 80) name = name.Substring(0, 80);
            if (name.Length == 0) return null;

            return Path.Combine(Folder, name + ".layout");
        }
    }
}
