using System;
using DataverseErdVisualizer.Models;
using DataverseErdVisualizer.Rendering;

namespace DataverseErdVisualizer.Layout
{
    /// <summary>
    /// Measures every entity box (header band + attribute rows) and truncates
    /// row text with an ellipsis so it fits the box width. Must run before the
    /// layout engine; only Bounds.Width/Height are set here.
    /// </summary>
    public static class ErdNodeSizer
    {
        public static void Size(ErdGraph graph, IDiagramSurface s)
        {
            foreach (var node in graph.Nodes)
                SizeNode(node, s);
        }

        private static void SizeNode(ErdNode node, IDiagramSurface s)
        {
            float titleW = s.MeasureString(node.Title ?? "", ErdStyle.HeaderFont).Width;
            float subW = string.IsNullOrEmpty(node.Subtitle)
                ? 0f : s.MeasureString(node.Subtitle, ErdStyle.HeaderSubFont).Width;

            // Badge space is always reserved so attribute names align.
            float badge = ErdStyle.BadgeWidth + ErdStyle.BadgeGap;
            float rowW = 0f;
            foreach (var row in node.Rows)
            {
                float w = badge + s.MeasureString(row.Name ?? "", FontFor(row)).Width;
                if (!string.IsNullOrEmpty(row.Type))
                    w += ErdStyle.TypeGap + s.MeasureString(row.Type, ErdStyle.RowFont).Width;
                if (w > rowW) rowW = w;
            }

            float content = Math.Max(Math.Max(titleW, subW), rowW);
            float width = content + 2f * ErdStyle.NodePadX;
            if (width < ErdStyle.NodeMinWidth) width = ErdStyle.NodeMinWidth;
            if (width > ErdStyle.NodeMaxWidth) width = ErdStyle.NodeMaxWidth;

            node.HeaderHeight = 2f * ErdStyle.HeaderPadY + ErdStyle.HeaderLineHeight +
                                (string.IsNullOrEmpty(node.Subtitle) ? 0f : ErdStyle.HeaderSubLineHeight);

            int rowCount = node.Rows.Count + (node.MoreCount > 0 ? 1 : 0);
            float bodyHeight = rowCount > 0 ? 2f * ErdStyle.RowsPadY + rowCount * ErdStyle.RowHeight : 0f;

            node.Bounds = new System.Drawing.RectangleF(0f, 0f, width, node.HeaderHeight + bodyHeight);

            TruncateRows(node, s, width);
        }

        private static void TruncateRows(ErdNode node, IDiagramSurface s, float width)
        {
            float inner = width - 2f * ErdStyle.NodePadX;
            float avail = inner - (ErdStyle.BadgeWidth + ErdStyle.BadgeGap);

            foreach (var row in node.Rows)
            {
                string type = row.Type ?? "";
                float typeW = 0f;
                if (type.Length > 0)
                {
                    // The type column may take at most 45% of the row; the name wins.
                    row.DisplayType = Fit(type, ErdStyle.RowFont, avail * 0.45f, s);
                    typeW = s.MeasureString(row.DisplayType, ErdStyle.RowFont).Width + ErdStyle.TypeGap;
                }
                else
                {
                    row.DisplayType = "";
                }

                row.DisplayName = Fit(row.Name ?? "", FontFor(row), avail - typeW, s);
            }
        }

        /// <summary>Truncates text with a trailing ellipsis until it fits maxWidth.</summary>
        private static string Fit(string text, DiagramFont font, float maxWidth, IDiagramSurface s)
        {
            if (maxWidth <= 4f) return "";
            if (s.MeasureString(text, font).Width <= maxWidth) return text;

            int len = text.Length;
            while (len > 1)
            {
                len--;
                var candidate = text.Substring(0, len).TrimEnd() + "…";
                if (s.MeasureString(candidate, font).Width <= maxWidth)
                    return candidate;
            }
            return "…";
        }

        private static DiagramFont FontFor(ErdRow row)
            => row.Badge == RowBadge.PrimaryKey || row.Badge == RowBadge.PrimaryName
                ? ErdStyle.RowBoldFont
                : ErdStyle.RowFont;
    }
}
