using System;
using System.IO;

namespace DataverseErdVisualizer
{
    /// <summary>Where this tool keeps what it remembers between sessions.</summary>
    internal static class SettingsFolder
    {
        /// <summary>
        /// Beside XrmToolBox's own settings, so clearing or migrating an
        /// XrmToolBox profile takes this tool's preferences along with it.
        /// </summary>
        public static string Root => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MscrmTools", "XrmToolBox", "Settings", "DataverseErdVisualizer");

        /// <summary>
        /// The file one solution keeps under <c>Root\{kind}</c>, named after
        /// the solution; null when there is no usable name.
        /// </summary>
        public static string PerSolutionFile(string kind, string solutionKey, string extension)
        {
            if (string.IsNullOrWhiteSpace(solutionKey)) return null;

            var name = solutionKey.Trim();
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (name.Length > 80) name = name.Substring(0, 80);
            if (name.Length == 0) return null;

            return Path.Combine(Root, kind, name + extension);
        }
    }
}
