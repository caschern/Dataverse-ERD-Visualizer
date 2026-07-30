using System.Drawing;
using DataverseErdVisualizer.Models;

namespace DataverseErdVisualizer.Rendering
{
    /// <summary>A font specification independent of GDI+/PdfSharp.</summary>
    public struct DiagramFont
    {
        public string Family;
        public float Size;
        public bool Bold;

        public DiagramFont(string family, float size, bool bold)
        {
            Family = family;
            Size = size;
            Bold = bold;
        }
    }

    /// <summary>Resolved visual style for an entity box flavor.</summary>
    public struct NodeStyle
    {
        public Color HeaderFill;
        public Color HeaderText;
        public Color HeaderSubText;
        public Color Border;
        public Color BodyFill;
        public Color RowText;
        public Color RowTypeText;
    }

    /// <summary>
    /// Central palette and sizing constants so the on-screen, PDF, SVG and
    /// HTML renderings stay visually consistent.
    /// </summary>
    public static class ErdStyle
    {
        public const string FontFamily = "Segoe UI";

        public static DiagramFont TitleFont => new DiagramFont(FontFamily, 13f, true);
        public static DiagramFont SubtitleFont => new DiagramFont(FontFamily, 7.5f, false);
        public static DiagramFont HeaderFont => new DiagramFont(FontFamily, 9.5f, true);
        public static DiagramFont HeaderSubFont => new DiagramFont(FontFamily, 7f, false);
        public static DiagramFont RowFont => new DiagramFont(FontFamily, 8f, false);
        public static DiagramFont RowBoldFont => new DiagramFont(FontFamily, 8f, true);
        public static DiagramFont BadgeFont => new DiagramFont(FontFamily, 6f, true);
        public static DiagramFont EdgeLabelFont => new DiagramFont(FontFamily, 8f, false);
        public static DiagramFont MoreFont => new DiagramFont(FontFamily, 7.5f, false);

        // Layout constants (device-independent units == pixels == points)
        public const float NodeMinWidth = 150f;
        public const float NodeMaxWidth = 250f;
        public const float NodePadX = 10f;
        public const float HeaderPadY = 6f;
        public const float HeaderLineHeight = 15f;
        public const float HeaderSubLineHeight = 11f;
        public const float RowHeight = 15f;
        public const float RowsPadY = 4f;
        public const float BadgeWidth = 20f;
        public const float BadgeGap = 4f;
        public const float TypeGap = 8f;
        public const float HorizontalGap = 56f;
        public const float VerticalGap = 64f;
        public const float Margin = 30f;
        public const float TitleBandHeight = 46f;
        public const float CornerRadius = 6f;

        // Cardinality glyph geometry
        public const float CrowFootLength = 12f;
        public const float CrowFootHalfWidth = 7f;
        public const float OneTickOffset = 9f;
        public const float OneTickHalfWidth = 5f;

        // Parallel edges between the same pair fan out by this many units.
        public const float ParallelSpacing = 14f;

        public static readonly Color CanvasBackground = Color.White;
        public static readonly Color EdgeColor = Color.FromArgb(110, 118, 129);
        public static readonly Color ManyToManyEdgeColor = Color.FromArgb(123, 97, 163);
        public static readonly Color EdgeLabelColor = Color.FromArgb(55, 62, 70);
        public static readonly Color TitleColor = Color.FromArgb(33, 41, 54);
        public static readonly Color SubtitleColor = Color.FromArgb(110, 118, 129);
        public static readonly Color RowSeparator = Color.FromArgb(232, 235, 239);

        public static readonly Color BadgePk = Color.FromArgb(245, 159, 0);
        public static readonly Color BadgeName = Color.FromArgb(56, 142, 60);
        public static readonly Color BadgeFk = Color.FromArgb(25, 118, 210);

        public static NodeStyle For(NodeFlavor flavor)
        {
            switch (flavor)
            {
                case NodeFlavor.Custom:
                    return Header(Color.FromArgb(0, 120, 133));      // teal
                case NodeFlavor.Activity:
                    return Header(Color.FromArgb(142, 36, 170));     // purple
                case NodeFlavor.External:
                    return Header(Color.FromArgb(120, 128, 138));    // grey
                case NodeFlavor.Standard:
                default:
                    return Header(Color.FromArgb(37, 88, 158));      // blue
            }
        }

        private static NodeStyle Header(Color header)
        {
            return new NodeStyle
            {
                HeaderFill = header,
                HeaderText = Color.White,
                HeaderSubText = Color.FromArgb(215, Color.White),
                Border = Darken(header, 0.8f),
                BodyFill = Color.White,
                RowText = Color.FromArgb(38, 50, 56),
                RowTypeText = Color.FromArgb(130, 138, 148)
            };
        }

        private static Color Darken(Color c, float f)
        {
            return Color.FromArgb(c.A, (int)(c.R * f), (int)(c.G * f), (int)(c.B * f));
        }
    }
}
