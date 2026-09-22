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
    }
}
