using System.ComponentModel.Composition;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace DataverseErdVisualizer
{
    /// <summary>
    /// Factory discovered by XrmToolBox via MEF. The metadata drives the tile
    /// shown on the XrmToolBox home screen.
    /// </summary>
    [Export(typeof(IXrmToolBoxPlugin))]
    [ExportMetadata("Name", "Dataverse ERD Visualizer")]
    [ExportMetadata("Description", "Generate interactive Entity Relationship Diagrams from any solution: tables, columns, lookups and N:N relationships with crow's-foot notation. Filter out system noise, drag tables to fine-tune the layout, and export to PNG, SVG, PDF, an HTML data dictionary or a Mermaid erDiagram.")]
    [ExportMetadata("BackgroundColor", "White")]
    [ExportMetadata("PrimaryFontColor", "Black")]
    [ExportMetadata("SecondaryFontColor", "DarkGray")]
    [ExportMetadata("SmallImageBase64", SmallIconBase64)]
    [ExportMetadata("BigImageBase64", BigIconBase64)]
    public class ErdVisualizerPlugin : PluginBase, IGitHubPlugin
    {
        private const string SmallIconBase64 = "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAE+SURBVFhHY/g/wGDUAUQ54Ojx0//d/KJIxiB9hABRDli0bM1/0+Ca/6oR84jGIPUgfYTAqAOIdgAxhiEDYvUMLQe8fvvx/8Onrwji56/e0cYBpIDh5YB1G7dhLWgIYZA+QoAoB+ADeWWNUBZ5YPA74N27D/83bd3zf9uuA1gxyAHYxEEYpA+kHx8g6ICVa7f8nzRjIVYLQHj2ghVYxUEYpA+kHx8g6IBJM+b/75syB8ojDRCjF68DXrx8/b+6ufd/U+fk/9du3IaKEgeI1YvTAd++ff/fOWHm/7WbdoDjEsR+++49VBY/IEUv3AGxmcVY8zI+DNJDqV64A0CCDBWtJGGQHkr1jjoAxQGkAmQHkAoGpwN+/vqNtYGBDX/9/gPFAeTqHY0CuAOCIlPAgqRgkB5K9cIdMFBggB3w/z8ApSfRmyrFvfYAAAAASUVORK5CYII="; // 32x32

        private const string BigIconBase64 = "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"; // 120x120

        public override IXrmToolBoxPluginControl GetControl()
        {
            return new ErdVisualizerControl();
        }

        // IGitHubPlugin — surfaces an "Update" / help link in XrmToolBox.
        public string RepositoryName => "Dataverse-ERD-Visualizer";
        public string UserName => "caschern";
    }
}
