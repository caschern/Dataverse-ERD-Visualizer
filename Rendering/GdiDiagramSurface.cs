using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace DataverseErdVisualizer.Rendering
{
    /// <summary>GDI+ implementation of <see cref="IDiagramSurface"/> (screen preview and PNG export).</summary>
    public sealed class GdiDiagramSurface : IDiagramSurface, IDisposable
    {
        private readonly Graphics _g;
        private readonly Dictionary<string, Font> _fontCache = new Dictionary<string, Font>();

        public GdiDiagramSurface(Graphics g)
        {
            _g = g;
            _g.SmoothingMode = SmoothingMode.AntiAlias;
            _g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            _g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }

        private Font GetFont(DiagramFont f)
        {
            var key = f.Family + "|" + f.Size + "|" + f.Bold;
            if (!_fontCache.TryGetValue(key, out var font))
            {
                font = new Font(f.Family, f.Size, f.Bold ? FontStyle.Bold : FontStyle.Regular);
                _fontCache[key] = font;
            }
            return font;
        }

        public SizeF MeasureString(string text, DiagramFont font)
            => _g.MeasureString(text ?? "", GetFont(font), 10000, StringFormat.GenericTypographic);

        public void DrawString(string text, DiagramFont font, Color color, float x, float y)
        {
            using (var brush = new SolidBrush(color))
                _g.DrawString(text ?? "", GetFont(font), brush, x, y, StringFormat.GenericTypographic);
        }

        public void DrawStringRotated(string text, DiagramFont font, Color color, float x, float y)
        {
            var state = _g.Save();
            try
            {
                _g.TranslateTransform(x, y);
                _g.RotateTransform(-90f);
                using (var brush = new SolidBrush(color))
                    _g.DrawString(text ?? "", GetFont(font), brush, 0f, 0f, StringFormat.GenericTypographic);
            }
            finally
            {
                _g.Restore(state);
            }
        }

        public void DrawLine(Color color, float width, float x1, float y1, float x2, float y2, bool dashed)
        {
            using (var pen = new Pen(color, width))
            {
                if (dashed) pen.DashStyle = DashStyle.Dash;
                _g.DrawLine(pen, x1, y1, x2, y2);
            }
        }

        public void FillPolygon(Color fill, PointF[] points)
        {
            using (var brush = new SolidBrush(fill))
                _g.FillPolygon(brush, points);
        }

        public void DrawPolygon(Color stroke, float width, PointF[] points)
        {
            using (var pen = new Pen(stroke, width))
                _g.DrawPolygon(pen, points);
        }

        public void FillRoundedRect(Color fill, RectangleF rect, float radius)
        {
            using (var path = RoundedPath(rect, radius))
            using (var brush = new SolidBrush(fill))
                _g.FillPath(brush, path);
        }

        public void DrawRoundedRect(Color stroke, float width, RectangleF rect, float radius)
        {
            using (var path = RoundedPath(rect, radius))
            using (var pen = new Pen(stroke, width))
                _g.DrawPath(pen, path);
        }

        public void FillEllipse(Color fill, RectangleF rect)
        {
            using (var brush = new SolidBrush(fill))
                _g.FillEllipse(brush, rect);
        }

        public void DrawEllipse(Color stroke, float width, RectangleF rect)
        {
            using (var pen = new Pen(stroke, width))
                _g.DrawEllipse(pen, rect);
        }

        private static GraphicsPath RoundedPath(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var path = new GraphicsPath();
            if (d <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public void Dispose()
        {
            foreach (var f in _fontCache.Values) f.Dispose();
            _fontCache.Clear();
        }
    }
}
