using DataverseErdVisualizer.Models;
using DataverseErdVisualizer.Rendering;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace DataverseErdVisualizer.Exporters
{
    /// <summary>Writes the diagram as a single-page vector PDF sized to the canvas.</summary>
    public static class PdfExporter
    {
        public static void Save(ErdDiagram diagram, string path)
        {
            using (var document = new PdfDocument())
            {
                document.Info.Title = diagram.Graph.Title ?? "Entity Relationship Diagram";
                document.Info.Creator = "Dataverse ERD Visualizer";

                var page = document.AddPage();
                page.Width = XUnit.FromPoint(diagram.CanvasSize.Width);
                page.Height = XUnit.FromPoint(diagram.CanvasSize.Height);

                using (var g = XGraphics.FromPdfPage(page))
                {
                    var surface = new PdfDiagramSurface(g);
                    ErdRenderer.Render(surface, diagram.Graph, diagram.CanvasSize);
                }

                document.Save(path);
            }
        }
    }
}
