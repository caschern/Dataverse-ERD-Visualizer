using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;

namespace DataverseErdVisualizer.Rendering
{
    /// <summary>
    /// SVG implementation of <see cref="IDiagramSurface"/> (vector export that
    /// Visio opens as editable shapes and Mural/Figma/draw.io accept as import).
    /// Text is measured with GDI+ so geometry matches the on-screen preview;
    /// SVG font sizes are emitted in px (pt * 96/72) to keep the same scale.
    /// All numbers use the invariant culture so output is locale-safe.
    /// </summary>
    public sealed class SvgDiagramSurface : IDiagramSurface, IDisposable
    {
        private readonly StringBuilder _sb = new StringBuilder();
        private readonly Bitmap _measureBitmap = new Bitmap(1, 1);
        private readonly Graphics _measure;
        private readonly Dictionary<string, Font> _fontCache = new Dictionary<string, Font>();

        public SvgDiagramSurface()
        {
            _measure = Graphics.FromImage(_measureBitmap);
        }

        /// <summary>The SVG elements drawn so far (no outer &lt;svg&gt; wrapper).</summary>
        public string GetElements() => _sb.ToString();

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
            => _measure.MeasureString(text ?? "", GetFont(font), 10000, StringFormat.GenericTypographic);

        public void DrawString(string text, DiagramFont font, Color color, float x, float y)
        {
            if (string.IsNullOrEmpty(text)) return;

            var gdiFont = GetFont(font);
            float sizePx = font.Size * 96f / 72f;

            // SVG text is positioned by baseline; convert from the top-left
            // convention using the font's ascent.
            var family = gdiFont.FontFamily;
            float ascent = sizePx * family.GetCellAscent(gdiFont.Style) / family.GetEmHeight(gdiFont.Style);

            _sb.Append("<text x=\"").Append(F(x)).Append("\" y=\"").Append(F(y + ascent))
               .Append("\" font-family=\"").Append(Escape(font.Family)).Append(", Arial, sans-serif\"")
               .Append(" font-size=\"").Append(F(sizePx)).Append("\"");
            if (font.Bold) _sb.Append(" font-weight=\"bold\"");
            _sb.Append(" fill=\"").Append(Hex(color)).Append("\">")
               .Append(Escape(text)).AppendLine("</text>");
        }

        public void DrawStringRotated(string text, DiagramFont font, Color color, float x, float y)
        {
            if (string.IsNullOrEmpty(text)) return;

            var gdiFont = GetFont(font);
            float sizePx = font.Size * 96f / 72f;
            var family = gdiFont.FontFamily;
            float ascent = sizePx * family.GetCellAscent(gdiFont.Style) / family.GetEmHeight(gdiFont.Style);

            _sb.Append("<text x=\"0\" y=\"").Append(F(ascent))
               .Append("\" transform=\"translate(").Append(F(x)).Append(' ').Append(F(y)).Append(") rotate(-90)\"")
               .Append(" font-family=\"").Append(Escape(font.Family)).Append(", Arial, sans-serif\"")
               .Append(" font-size=\"").Append(F(sizePx)).Append("\"");
            if (font.Bold) _sb.Append(" font-weight=\"bold\"");
            _sb.Append(" fill=\"").Append(Hex(color)).Append("\">")
               .Append(Escape(text)).AppendLine("</text>");
        }

        public void DrawLine(Color color, float width, float x1, float y1, float x2, float y2, bool dashed)
        {
            _sb.Append("<line x1=\"").Append(F(x1)).Append("\" y1=\"").Append(F(y1))
               .Append("\" x2=\"").Append(F(x2)).Append("\" y2=\"").Append(F(y2))
               .Append("\" stroke=\"").Append(Hex(color))
               .Append("\" stroke-width=\"").Append(F(width)).Append("\"");
            if (dashed) _sb.Append(" stroke-dasharray=\"4 3\"");
            _sb.AppendLine(" fill=\"none\"/>");
        }

        public void FillPolygon(Color fill, PointF[] points)
        {
            _sb.Append("<polygon points=\"").Append(Points(points))
               .Append("\" fill=\"").Append(Hex(fill)).Append("\"").Append(Opacity(fill)).AppendLine("/>");
        }

        public void DrawPolygon(Color stroke, float width, PointF[] points)
        {
            _sb.Append("<polygon points=\"").Append(Points(points))
               .Append("\" fill=\"none\" stroke=\"").Append(Hex(stroke))
               .Append("\" stroke-width=\"").Append(F(width)).AppendLine("\"/>");
        }

        public void FillRoundedRect(Color fill, RectangleF rect, float radius)
        {
            _sb.Append(RectOpen(rect, radius))
               .Append(" fill=\"").Append(Hex(fill)).Append("\"").Append(Opacity(fill)).AppendLine("/>");
        }

        public void DrawRoundedRect(Color stroke, float width, RectangleF rect, float radius)
        {
            _sb.Append(RectOpen(rect, radius))
               .Append(" fill=\"none\" stroke=\"").Append(Hex(stroke))
               .Append("\" stroke-width=\"").Append(F(width)).AppendLine("\"/>");
        }

        public void FillEllipse(Color fill, RectangleF rect)
        {
            _sb.Append(EllipseOpen(rect))
               .Append(" fill=\"").Append(Hex(fill)).Append("\"").Append(Opacity(fill)).AppendLine("/>");
        }

        public void DrawEllipse(Color stroke, float width, RectangleF rect)
        {
            _sb.Append(EllipseOpen(rect))
               .Append(" fill=\"none\" stroke=\"").Append(Hex(stroke))
               .Append("\" stroke-width=\"").Append(F(width)).AppendLine("\"/>");
        }

        // ------------------------------------------------------------ helpers

        private static string RectOpen(RectangleF r, float radius)
        {
            // Match the GDI+ clamp: the corner diameter never exceeds the side.
            float rx = Math.Min(radius, Math.Min(r.Width, r.Height) / 2f);
            return "<rect x=\"" + F(r.X) + "\" y=\"" + F(r.Y) +
                   "\" width=\"" + F(r.Width) + "\" height=\"" + F(r.Height) +
                   "\" rx=\"" + F(rx) + "\" ry=\"" + F(rx) + "\"";
        }

        private static string EllipseOpen(RectangleF r)
        {
            return "<ellipse cx=\"" + F(r.X + r.Width / 2f) + "\" cy=\"" + F(r.Y + r.Height / 2f) +
                   "\" rx=\"" + F(r.Width / 2f) + "\" ry=\"" + F(r.Height / 2f) + "\"";
        }

        private static string Points(PointF[] pts)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < pts.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(F(pts[i].X)).Append(',').Append(F(pts[i].Y));
            }
            return sb.ToString();
        }

        private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        private static string Hex(Color c)
            => "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");

        private static string Opacity(Color c)
            => c.A == 255 ? "" : " fill-opacity=\"" + (c.A / 255f).ToString("0.##", CultureInfo.InvariantCulture) + "\"";

        private static string Escape(string s)
        {
            return s.Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;");
        }

        public void Dispose()
        {
            foreach (var f in _fontCache.Values) f.Dispose();
            _fontCache.Clear();
            _measure.Dispose();
            _measureBitmap.Dispose();
        }
    }
}
