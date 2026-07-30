using System.Drawing;
using System.Drawing.Imaging;
using DataverseErdVisualizer.Models;
using DataverseErdVisualizer.Rendering;

namespace DataverseErdVisualizer.Exporters
{
    /// <summary>Renders the diagram to a high-resolution PNG via GDI+.</summary>
    public static class PngExporter
    {
        public static Bitmap RenderToBitmap(ErdDiagram diagram, float scale = 2f)
        {
            int w = (int)(diagram.CanvasSize.Width * scale) + 1;
            int h = (int)(diagram.CanvasSize.Height * scale) + 1;
            var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.ScaleTransform(scale, scale);
                using (var surface = new GdiDiagramSurface(g))
                    ErdRenderer.Render(surface, diagram.Graph, diagram.CanvasSize);
            }
            return bmp;
        }

        public static void Save(ErdDiagram diagram, string path, float scale = 2f)
        {
            using (var bmp = RenderToBitmap(diagram, scale))
                bmp.Save(path, ImageFormat.Png);
        }
    }
}
