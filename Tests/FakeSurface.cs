using System.Drawing;
using DataverseErdVisualizer.Rendering;

namespace DataverseErdVisualizer.Tests
{
    /// <summary>
    /// Deterministic measuring surface so sizing/layout tests don't depend on
    /// installed fonts or a graphics device. Draw calls are no-ops.
    /// </summary>
    public class FakeSurface : IDiagramSurface
    {
        public SizeF MeasureString(string text, DiagramFont font)
            => new SizeF((text ?? "").Length * font.Size * 0.6f, font.Size * 1.5f);

        public void DrawString(string text, DiagramFont font, Color color, float x, float y) { }
        public void DrawStringRotated(string text, DiagramFont font, Color color, float x, float y) { }
        public void DrawLine(Color color, float width, float x1, float y1, float x2, float y2, bool dashed) { }
        public void FillPolygon(Color fill, PointF[] points) { }
        public void DrawPolygon(Color stroke, float width, PointF[] points) { }
        public void FillRoundedRect(Color fill, RectangleF rect, float radius) { }
        public void DrawRoundedRect(Color stroke, float width, RectangleF rect, float radius) { }
        public void FillEllipse(Color fill, RectangleF rect) { }
        public void DrawEllipse(Color stroke, float width, RectangleF rect) { }
    }
}
