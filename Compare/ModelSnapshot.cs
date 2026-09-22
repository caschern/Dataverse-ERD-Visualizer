using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using DataverseErdVisualizer.Models;

namespace DataverseErdVisualizer.Compare
{
    /// <summary>
    /// A solution's data model frozen at a moment in time, saved to a file so
    /// it can be compared later — against another environment, or against the
    /// same one after a release.
    ///
    /// A file rather than a second live connection: it works across
    /// environments and across time, offline, and needs none of the host's
    /// multi-connection API. The comparison itself does not care where either
    /// side came from.
    /// </summary>
    [XmlRoot("ErdSnapshot")]
    public class ModelSnapshot
    {
        /// <summary>Bumped when the file layout changes incompatibly.</summary>
        public const int CurrentFormat = 1;

        [XmlAttribute]
        public int Format { get; set; } = CurrentFormat;

        /// <summary>When the model was captured (UTC).</summary>
        public DateTime CapturedOn { get; set; }

        /// <summary>Which environment it came from, as the connection named it.</summary>
        public string Environment { get; set; }

        public string ToolVersion { get; set; }

        public ErdModel Model { get; set; }

        /// <summary>"Prod · v2026.08.01.1 · 2026-09-01 14:02" — whatever is known.</summary>
        public string Describe()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(Environment)) parts.Add(Environment);
            if (!string.IsNullOrEmpty(Model?.Solution?.Version)) parts.Add("v" + Model.Solution.Version);
            if (CapturedOn != default(DateTime))
                parts.Add(CapturedOn.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            return parts.Count == 0 ? "unknown" : string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// Reads and writes snapshot files. XML through the framework's own
    /// serializer: the host supplies an older Newtonsoft at runtime, and this
    /// codebase has been bitten by that before.
    ///
    /// Unlike the settings stores, failures here are REPORTED: a snapshot is a
    /// file someone chose on purpose, and silently comparing against nothing
    /// would claim every table was added.
    /// </summary>
    public static class SnapshotStore
    {
        public const string Extension = ".erdsnapshot";
        public const string DialogFilter = "ERD model snapshot (*.erdsnapshot)|*.erdsnapshot";

        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(ModelSnapshot));

        public static void Save(ModelSnapshot snapshot, string path)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
            using (var writer = XmlWriter.Create(path, settings))
                Serializer.Serialize(writer, snapshot);
        }

        public static ModelSnapshot Load(string path)
        {
            ModelSnapshot snapshot;
            try
            {
                using (var reader = XmlReader.Create(path))
                    snapshot = (ModelSnapshot)Serializer.Deserialize(reader);
            }
            catch (Exception ex) when (!(ex is FileNotFoundException))
            {
                throw new InvalidDataException(
                    "This file is not a readable ERD model snapshot: " + Path.GetFileName(path), ex);
            }

            if (snapshot?.Model == null)
                throw new InvalidDataException(
                    "This snapshot contains no data model: " + Path.GetFileName(path));

            if (snapshot.Format > ModelSnapshot.CurrentFormat)
                throw new InvalidDataException(
                    "This snapshot was made by a newer version of the tool. Update to read it.");

            return snapshot;
        }
    }
}
