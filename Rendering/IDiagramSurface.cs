using System.Drawing;

namespace DataverseErdVisualizer.Rendering
{
    /// <summary>
    /// Low-level drawing primitives, implemented once for GDI+ (screen / PNG),
    /// once for SVG and once for PdfSharp (vector PDF). All coordinates are in
    /// device-independent units with the origin at the top-left. The high-level
    /// box, row and cardinality-glyph logic lives in <see cref="ErdRenderer"/>
    /// so the backends stay pixel-consistent.
    /// </summary>
    public interface IDiagramSurface
    {
        SizeF MeasureString(string text, DiagramFont font);

        /// <summary>Draws text with its top-left at (x, y).</summary>
        void DrawString(string text, DiagramFont font, Color color, float x, float y);

        /// <summary>
        /// Draws text rotated 90° counter-clockwise (reading bottom-to-top).
        /// Equivalent to translating the origin to (x, y), rotating -90°, then
        /// drawing at (0,0): the first character's top-left corner sits at
        /// (x, y) and the text climbs upward, line-height extending to +X.
        /// </summary>
        void DrawStringRotated(string text, DiagramFont font, Color color, float x, float y);

        void DrawLine(Color color, float width, float x1, float y1, float x2, float y2, bool dashed);

        void FillPolygon(Color fill, PointF[] points);
        void DrawPolygon(Color stroke, float width, PointF[] points);

        void FillRoundedRect(Color fill, RectangleF rect, float radius);
        void DrawRoundedRect(Color stroke, float width, RectangleF rect, float radius);

        void FillEllipse(Color fill, RectangleF rect);
        void DrawEllipse(Color stroke, float width, RectangleF rect);
    }
}
