using System.Globalization;
using System.IO;
using System.Text;
using DataverseErdVisualizer.Models;
using DataverseErdVisualizer.Rendering;

namespace DataverseErdVisualizer.Exporters
{
    /// <summary>
    /// Writes the diagram as a standalone SVG file (opens as editable vector
    /// shapes in Visio, draw.io, Figma, browsers…).
    /// </summary>
    public static class SvgExporter
    {
        public static string Generate(ErdDiagram diagram)
        {
            string elements;
            using (var surface = new SvgDiagramSurface())
            {
                ErdRenderer.Render(surface, diagram.Graph, diagram.CanvasSize);
                elements = surface.GetElements();
            }

            string w = diagram.CanvasSize.Width.ToString("0.##", CultureInfo.InvariantCulture);
            string h = diagram.CanvasSize.Height.ToString("0.##", CultureInfo.InvariantCulture);

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(w)
              .Append("\" height=\"").Append(h)
              .Append("\" viewBox=\"0 0 ").Append(w).Append(' ').Append(h).AppendLine("\">");
            sb.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#FFFFFF\"/>");
            sb.Append(elements);
            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        public static void Save(ErdDiagram diagram, string path)
            => File.WriteAllText(path, Generate(diagram), Encoding.UTF8);
    }
}
