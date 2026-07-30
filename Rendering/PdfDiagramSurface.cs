using System.Collections.Generic;
using System.Drawing;
using PdfSharp.Drawing;

namespace DataverseErdVisualizer.Rendering
{
    /// <summary>PdfSharp implementation of <see cref="IDiagramSurface"/> (vector PDF export).</summary>
    public sealed class PdfDiagramSurface : IDiagramSurface
    {
        private readonly XGraphics _g;
        private readonly Dictionary<string, XFont> _fontCache = new Dictionary<string, XFont>();

        public PdfDiagramSurface(XGraphics g)
        {
            _g = g;
            _g.SmoothingMode = XSmoothingMode.HighQuality;
        }

        private XFont GetFont(DiagramFont f)
        {
            var key = f.Family + "|" + f.Size + "|" + f.Bold;
            if (!_fontCache.TryGetValue(key, out var font))
            {
                font = new XFont(f.Family, f.Size, f.Bold ? XFontStyle.Bold : XFontStyle.Regular);
                _fontCache[key] = font;
            }
            return font;
        }

        private static XColor C(Color c) => XColor.FromArgb(c.A, c.R, c.G, c.B);

        private static XPoint[] P(PointF[] pts)
        {
            var arr = new XPoint[pts.Length];
            for (int i = 0; i < pts.Length; i++) arr[i] = new XPoint(pts[i].X, pts[i].Y);
            return arr;
        }

        public SizeF MeasureString(string text, DiagramFont font)
        {
            var size = _g.MeasureString(text ?? "", GetFont(font));
            return new SizeF((float)size.Width, (float)size.Height);
        }

        public void DrawString(string text, DiagramFont font, Color color, float x, float y)
        {
            var rect = new XRect(x, y, 100000, 100000);
            _g.DrawString(text ?? "", GetFont(font), new XSolidBrush(C(color)), rect, XStringFormats.TopLeft);
        }

        public void DrawStringRotated(string text, DiagramFont font, Color color, float x, float y)
        {
            var state = _g.Save();
            try
            {
                _g.TranslateTransform(x, y);
                _g.RotateTransform(-90);
                var rect = new XRect(0, 0, 100000, 100000);
                _g.DrawString(text ?? "", GetFont(font), new XSolidBrush(C(color)), rect, XStringFormats.TopLeft);
            }
            finally
            {
                _g.Restore(state);
            }
        }

        public void DrawLine(Color color, float width, float x1, float y1, float x2, float y2, bool dashed)
        {
            var pen = new XPen(C(color), width);
            if (dashed) pen.DashStyle = XDashStyle.Dash;
            _g.DrawLine(pen, x1, y1, x2, y2);
        }

        public void FillPolygon(Color fill, PointF[] points)
            => _g.DrawPolygon(new XSolidBrush(C(fill)), P(points), XFillMode.Winding);

        public void DrawPolygon(Color stroke, float width, PointF[] points)
            => _g.DrawPolygon(new XPen(C(stroke), width), P(points));

        public void FillRoundedRect(Color fill, RectangleF rect, float radius)
            => _g.DrawRoundedRectangle(new XSolidBrush(C(fill)),
                   rect.X, rect.Y, rect.Width, rect.Height, radius * 2, radius * 2);

        public void DrawRoundedRect(Color stroke, float width, RectangleF rect, float radius)
            => _g.DrawRoundedRectangle(new XPen(C(stroke), width),
                   rect.X, rect.Y, rect.Width, rect.Height, radius * 2, radius * 2);

        public void FillEllipse(Color fill, RectangleF rect)
            => _g.DrawEllipse(new XSolidBrush(C(fill)), rect.X, rect.Y, rect.Width, rect.Height);

        public void DrawEllipse(Color stroke, float width, RectangleF rect)
            => _g.DrawEllipse(new XPen(C(stroke), width), rect.X, rect.Y, rect.Width, rect.Height);
    }
}
