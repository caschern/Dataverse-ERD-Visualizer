using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using DataverseErdVisualizer.Layout;
using DataverseErdVisualizer.Models;

namespace DataverseErdVisualizer.Rendering
{
    /// <summary>
    /// Backend-independent drawing of an <see cref="ErdGraph"/>: title band,
    /// relationship lines with crow's-foot cardinality glyphs and label chips,
    /// then the entity boxes (header band + attribute rows with badges).
    /// </summary>
    public static class ErdRenderer
    {
        // Selection highlighting: the selected table's edges pop, the rest recede.
        private static readonly Color HighlightEdgeColor = Color.FromArgb(37, 118, 220);
        private static readonly Color DimEdgeColor = Color.FromArgb(214, 218, 224);
        private static readonly Color DimLabelColor = Color.FromArgb(196, 201, 208);

        public static void Render(IDiagramSurface surface, ErdGraph graph, SizeF canvas,
            bool interactive = false, string highlightId = null)
        {
            DrawTitle(surface, graph);

            bool highlight = interactive && !string.IsNullOrEmpty(highlightId);
            var labels = new List<EdgeLabel>();
            var portLabels = new List<PortLabel>();

            // Highlighted edges draw last so they sit on top of dimmed ones.
            IEnumerable<ErdEdge> ordered = graph.Edges;
            if (highlight)
                ordered = graph.Edges.OrderBy(e => IsIncident(e, highlightId) ? 1 : 0);

            foreach (var edge in ordered)
            {
                bool incident = highlight && IsIncident(edge, highlightId);
                var baseColor = edge.Kind == RelationshipKind.ManyToMany
                    ? ErdStyle.ManyToManyEdgeColor : ErdStyle.EdgeColor;
                var stroke = highlight
                    ? (incident ? HighlightEdgeColor : DimEdgeColor)
                    : baseColor;
                var labelColor = highlight
                    ? (incident ? ErdStyle.EdgeLabelColor : DimLabelColor)
                    : ErdStyle.EdgeLabelColor;
                float width = incident ? 2.2f : 1.4f;

                DrawEdge(surface, graph, edge, labels, portLabels, stroke, width, labelColor);
            }

            foreach (var node in graph.Nodes)
                DrawNode(surface, node);

            // If two label chips collide, slide the later one to the RIGHT along
            // its lane — the gap bands are node-free, the rows below are not.
            for (int i = 1; i < labels.Count; i++)
            {
                bool moved = true;
                int guard = 0;
                while (moved && guard++ < 8)
                {
                    moved = false;
                    for (int j = 0; j < i; j++)
                    {
                        if (labels[i].Backing.IntersectsWith(labels[j].Backing))
                        {
                            var shifted = labels[i];
                            shifted.Backing.X = labels[j].Backing.Right + 4f;
                            labels[i] = shifted;
                            moved = true;
                        }
                    }
                }
            }

            // Labels go last so no connector line can cross their text. Chips
            // that slid or anchored past the canvas edge are pulled back in.
            foreach (var label in labels)
            {
                var backing = label.Backing;
                if (backing.Right > canvas.Width - 2f) backing.X = canvas.Width - 2f - backing.Width;
                if (backing.X < 2f) backing.X = 2f;
                if (backing.Bottom > canvas.Height - 2f) backing.Y = canvas.Height - 2f - backing.Height;
                surface.FillRoundedRect(Color.White, backing, 3f);
                surface.DrawString(label.Text, ErdStyle.EdgeLabelFont, label.Color,
                    backing.X + 3f, backing.Y + 1f);
            }

            // Port labels: rotated 90°, climbing the connector's final drop
            // into its child table, so every label touches the line it names.
            // The backing is kept inside the port pitch so it can never paint
            // over the neighboring port's line.
            foreach (var pl in portLabels)
            {
                surface.FillRoundedRect(Color.White,
                    new RectangleF(pl.X - 0.5f, pl.Y - pl.TextWidth - 1f, pl.TextHeight, pl.TextWidth + 2f), 2f);
                surface.DrawStringRotated(pl.Text, ErdStyle.EdgeLabelFont, pl.Color, pl.X, pl.Y);
            }
        }

        private struct PortLabel
        {
            public string Text;
            public float X;          // left edge of the vertical text column
            public float Y;          // bottom of the text (just above the box)
            public float TextWidth;  // length of the text along the climb
            public float TextHeight; // line height (column thickness)
            public Color Color;
        }

        private static bool IsIncident(ErdEdge e, string id)
            => string.Equals(e.FromId, id, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(e.ToId, id, StringComparison.OrdinalIgnoreCase);

        private struct EdgeLabel
        {
            public string Text;
            public RectangleF Backing;
            public Color Color;
        }

        private static void DrawTitle(IDiagramSurface s, ErdGraph graph)
        {
            if (!string.IsNullOrEmpty(graph.Title))
                s.DrawString(graph.Title, ErdStyle.TitleFont, ErdStyle.TitleColor,
                    ErdStyle.Margin, ErdStyle.Margin - 6);

            if (!string.IsNullOrEmpty(graph.Subtitle))
                s.DrawString(graph.Subtitle, ErdStyle.SubtitleFont, ErdStyle.SubtitleColor,
                    ErdStyle.Margin, ErdStyle.Margin + 16);
        }

        // ---------- entity boxes ----------

        private static void DrawNode(IDiagramSurface s, ErdNode node)
        {
            var style = ErdStyle.For(node.Flavor);
            var r = node.Bounds;
            float radius = ErdStyle.CornerRadius;

            // Body, then header band (rounded top, squared bottom), then border.
            s.FillRoundedRect(style.BodyFill, r, radius);

            var headerRect = new RectangleF(r.X, r.Y, r.Width, node.HeaderHeight + radius);
            s.FillRoundedRect(style.HeaderFill, headerRect, radius);
            s.FillPolygon(style.BodyFill, RectPoints(
                new RectangleF(r.X, r.Y + node.HeaderHeight, r.Width, radius)));

            // Header text.
            float ty = r.Y + ErdStyle.HeaderPadY;
            s.DrawString(TruncateStatic(node.Title), ErdStyle.HeaderFont, style.HeaderText,
                r.X + ErdStyle.NodePadX, ty);
            if (!string.IsNullOrEmpty(node.Subtitle))
                s.DrawString(node.Subtitle, ErdStyle.HeaderSubFont, style.HeaderSubText,
                    r.X + ErdStyle.NodePadX, ty + ErdStyle.HeaderLineHeight);

            // Attribute rows.
            float y = r.Y + node.HeaderHeight + ErdStyle.RowsPadY;
            foreach (var row in node.Rows)
            {
                DrawRow(s, node, row, y, style);
                y += ErdStyle.RowHeight;
            }

            if (node.MoreCount > 0)
            {
                s.DrawString("+ " + node.MoreCount + " more…", ErdStyle.MoreFont, style.RowTypeText,
                    r.X + ErdStyle.NodePadX + ErdStyle.BadgeWidth + ErdStyle.BadgeGap, y + 1f);
            }

            s.DrawRoundedRect(style.Border, 1.5f, r, radius);

            // Hairline under the header band.
            if (node.Rows.Count > 0 || node.MoreCount > 0)
                s.DrawLine(style.Border, 1f, r.X, r.Y + node.HeaderHeight,
                    r.Right, r.Y + node.HeaderHeight, false);
        }

        private static void DrawRow(IDiagramSurface s, ErdNode node, ErdRow row, float y, NodeStyle style)
        {
            var r = node.Bounds;
            float x = r.X + ErdStyle.NodePadX;

            if (row.Badge != RowBadge.None)
            {
                var badgeRect = new RectangleF(x, y + 2f, ErdStyle.BadgeWidth, ErdStyle.RowHeight - 4f);
                var color = BadgeColor(row.Badge);
                s.FillRoundedRect(color, badgeRect, 3f);
                string text = BadgeText(row.Badge);
                var size = s.MeasureString(text, ErdStyle.BadgeFont);
                s.DrawString(text, ErdStyle.BadgeFont, Color.White,
                    badgeRect.X + (badgeRect.Width - size.Width) / 2f,
                    badgeRect.Y + (badgeRect.Height - size.Height) / 2f);
            }

            var font = row.Badge == RowBadge.PrimaryKey || row.Badge == RowBadge.PrimaryName
                ? ErdStyle.RowBoldFont : ErdStyle.RowFont;
            s.DrawString(row.DisplayName ?? row.Name, font, style.RowText,
                x + ErdStyle.BadgeWidth + ErdStyle.BadgeGap, y + 1f);

            var type = row.DisplayType ?? row.Type;
            if (!string.IsNullOrEmpty(type))
            {
                var size = s.MeasureString(type, ErdStyle.RowFont);
                s.DrawString(type, ErdStyle.RowFont, style.RowTypeText,
                    r.Right - ErdStyle.NodePadX - size.Width, y + 1f);
            }
        }

        private static Color BadgeColor(RowBadge badge)
        {
            switch (badge)
            {
                case RowBadge.PrimaryKey: return ErdStyle.BadgePk;
                case RowBadge.PrimaryName: return ErdStyle.BadgeName;
                default: return ErdStyle.BadgeFk;
            }
        }

        private static string BadgeText(RowBadge badge)
        {
            switch (badge)
            {
                case RowBadge.PrimaryKey: return "PK";
                case RowBadge.PrimaryName: return "N";
                default: return "FK";
            }
        }

        private static PointF[] RectPoints(RectangleF r) => new[]
        {
            new PointF(r.X, r.Y),
            new PointF(r.Right, r.Y),
            new PointF(r.Right, r.Bottom),
            new PointF(r.X, r.Bottom)
        };

        private static string TruncateStatic(string text)
            => text ?? "";

        // ---------- edges ----------

        private static void DrawEdge(IDiagramSurface s, ErdGraph graph, ErdEdge edge,
            List<EdgeLabel> labels, List<PortLabel> portLabels, Color stroke, float width, Color labelColor)
        {
            var from = graph[edge.FromId];
            var to = graph[edge.ToId];
            if (from == null || to == null) return;

            PointF[] path;
            bool dashed = edge.Kind == RelationshipKind.ManyToMany;

            if (edge.IsSelf)
            {
                path = SelfLoopPath(from, edge);
            }
            else if (edge.IsBack)
            {
                // Route cycle edges down the right-hand side, on their assigned rail.
                float offF = ErdLayoutEngine.AnchorOffset(edge, from);
                float offT = ErdLayoutEngine.AnchorOffset(edge, to);
                var start = new PointF(from.Bounds.Right, ClampY(from, from.Bounds.Y + from.Bounds.Height / 2f + offF));
                var end = new PointF(to.Bounds.Right, ClampY(to, to.Bounds.Y + to.Bounds.Height / 2f + offT));
                float bend = edge.RailX ?? (Math.Max(from.Bounds.Right, to.Bounds.Right) + 30f);
                path = new[]
                {
                    start,
                    new PointF(bend, start.Y),
                    new PointF(bend, end.Y),
                    end
                };
            }
            else
            {
                float sx = edge.FromPortX
                    ?? (from.Bounds.X + from.Bounds.Width / 2f + ErdLayoutEngine.AnchorOffset(edge, from));
                float tx = edge.ToPortX
                    ?? (to.Bounds.X + to.Bounds.Width / 2f + ErdLayoutEngine.AnchorOffset(edge, to));
                var start = new PointF(sx, from.Bounds.Bottom);
                var end = new PointF(tx, to.Bounds.Y);

                if (edge.Route != null && edge.Route.Count >= 2)
                {
                    // Multi-rank edge: already routed through reserved channels.
                    path = edge.Route.ToArray();
                }
                else if (Math.Abs(start.X - end.X) < 0.5f)
                {
                    path = new[] { start, end };
                }
                else
                {
                    // Orthogonal V-H-V route on the lane assigned by the layout.
                    float midY = edge.LaneY ?? ((start.Y + end.Y) / 2f);
                    path = new[]
                    {
                        start,
                        new PointF(start.X, midY),
                        new PointF(end.X, midY),
                        end
                    };
                }
            }

            for (int i = 0; i < path.Length - 1; i++)
                s.DrawLine(stroke, width, path[i].X, path[i].Y, path[i + 1].X, path[i + 1].Y, dashed);

            DrawCardinalityGlyphs(s, path, edge, stroke, width);

            if (!string.IsNullOrEmpty(edge.Label))
            {
                if (edge.IsSelf || edge.IsBack)
                    QueueLabel(s, path, edge, labels, labelColor);
                else
                    QueuePortLabel(s, path, edge, portLabels, labelColor);
            }
        }

        /// <summary>
        /// Rotated label hugging the right side of the connector's final drop,
        /// starting just above the child box it enters. Truncated to roughly
        /// the drop's length so it doesn't wander into the rank above.
        /// </summary>
        private static void QueuePortLabel(IDiagramSurface s, PointF[] path, ErdEdge edge,
            List<PortLabel> portLabels, Color color)
        {
            // Normally the label rides the drop INTO the target box. Cluster
            // edges running up into a shared hub set LabelAtSource, so the
            // label rides the satellite's own stub instead — otherwise every
            // label in the cluster would stack on the hub's single entry point.
            var stub = edge.LabelAtSource ? path[0] : path[path.Length - 1];
            var bend = edge.LabelAtSource
                ? path[Math.Min(1, path.Length - 1)]
                : path[path.Length - 2];

            float available, y;
            if (edge.LabelAtSource)
            {
                // Text climbs upward from Y, so it must stay between the stub
                // and the bend or it would run back over its own box.
                float span = Math.Abs(bend.Y - stub.Y);
                available = span - 10f;
                y = stub.Y + span - 4f;
            }
            else
            {
                // Room along the drop (starting above the crow's foot), allowed
                // to overshoot the bend; the white backing keeps it readable
                // over the lane bands it crosses.
                available = (stub.Y - 15f) - bend.Y + 40f;
                y = stub.Y - 15f; // clear of the crow's foot glyph
            }
            if (available < 34f) available = 34f;
            if (available > 175f) available = 175f;

            string text = Fit(s, edge.Label, ErdStyle.EdgeLabelFont, available);
            if (text.Length == 0) return;
            var size = s.MeasureString(text, ErdStyle.EdgeLabelFont);

            portLabels.Add(new PortLabel
            {
                Text = text,
                X = stub.X + 3f,
                Y = y,
                TextWidth = size.Width,
                TextHeight = size.Height,
                Color = color
            });
        }

        /// <summary>Truncates text with a trailing ellipsis until it fits maxWidth.</summary>
        private static string Fit(IDiagramSurface s, string text, DiagramFont font, float maxWidth)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 6f) return "";
            if (s.MeasureString(text, font).Width <= maxWidth) return text;

            int len = text.Length;
            while (len > 1)
            {
                len--;
                var candidate = text.Substring(0, len).TrimEnd() + "…";
                if (s.MeasureString(candidate, font).Width <= maxWidth)
                    return candidate;
            }
            return "";
        }

        private static float ClampY(ErdNode n, float y)
        {
            float top = n.Bounds.Y + 8f;
            float bottom = n.Bounds.Bottom - 8f;
            if (y < top) return top;
            if (y > bottom) return bottom;
            return y;
        }

        private static PointF[] SelfLoopPath(ErdNode n, ErdEdge edge)
        {
            // A small loop on the bottom-right corner of the box; parallel
            // self-loops stack upward and stick out further, and each label
            // chip gets its own row below the box (see QueueLabel).
            float extra = edge.ParallelCount > 1 ? edge.ParallelIndex * 18f : 0f;
            float y2 = Math.Max(n.Bounds.Bottom - 10f - extra, n.Bounds.Y + n.HeaderHeight + 20f);
            float y1 = Math.Max(y2 - 14f, n.Bounds.Y + n.HeaderHeight + 6f);
            float outX = n.Bounds.Right + 26f + (edge.ParallelCount > 1 ? edge.ParallelIndex * 8f : 0f);
            return new[]
            {
                new PointF(n.Bounds.Right, y1),
                new PointF(outX, y1),
                new PointF(outX, y2),
                new PointF(n.Bounds.Right, y2)
            };
        }

        /// <summary>
        /// Crow's-foot notation: a perpendicular tick at the "one" end (the path
        /// start), a three-prong foot at the "many" end (the path end). N:N gets
        /// feet at both ends. Glyphs orient along their segment.
        /// </summary>
        private static void DrawCardinalityGlyphs(IDiagramSurface s, PointF[] path, ErdEdge edge,
            Color color, float width)
        {
            if (path.Length < 2) return;

            var startDir = Direction(path[0], path[1]);
            var endDir = Direction(path[path.Length - 1], path[path.Length - 2]); // points back along the line

            if (edge.Kind == RelationshipKind.ManyToMany)
                DrawCrowFoot(s, path[0], startDir, color, width);
            else
                DrawOneTick(s, path[0], startDir, color, width);

            DrawCrowFoot(s, path[path.Length - 1], endDir, color, width);
        }

        private static PointF Direction(PointF a, PointF b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < 0.001) return new PointF(0f, 1f);
            return new PointF((float)(dx / dist), (float)(dy / dist));
        }

        /// <summary>Three prongs fanning from a point on the line to the node border.</summary>
        private static void DrawCrowFoot(IDiagramSurface s, PointF tip, PointF dir, Color color, float width)
        {
            float len = ErdStyle.CrowFootLength;
            float half = ErdStyle.CrowFootHalfWidth;
            var back = new PointF(tip.X + dir.X * len, tip.Y + dir.Y * len);
            var px = -dir.Y;
            var py = dir.X;

            s.DrawLine(color, width, back.X, back.Y, tip.X + px * half, tip.Y + py * half, false);
            s.DrawLine(color, width, back.X, back.Y, tip.X, tip.Y, false);
            s.DrawLine(color, width, back.X, back.Y, tip.X - px * half, tip.Y - py * half, false);
        }

        /// <summary>A short perpendicular bar crossing the line near the "one" end.</summary>
        private static void DrawOneTick(IDiagramSurface s, PointF end, PointF dir, Color color, float width)
        {
            float off = ErdStyle.OneTickOffset;
            float half = ErdStyle.OneTickHalfWidth;
            var center = new PointF(end.X + dir.X * off, end.Y + dir.Y * off);
            var px = -dir.Y;
            var py = dir.X;

            s.DrawLine(color, width, center.X + px * half, center.Y + py * half,
                center.X - px * half, center.Y - py * half, false);
        }

        private static void QueueLabel(IDiagramSurface s, PointF[] path, ErdEdge edge,
            List<EdgeLabel> labels, Color labelColor)
        {
            // Anchor the label to the FIRST horizontal run of the path: those lie
            // in the gaps between rows, which are node-free. Self-loops anchor
            // beside the loop instead.
            PointF mid;
            if (edge.IsSelf)
            {
                // Centered under the loop, below the box (the spot beside the
                // loop may belong to a neighboring box). Parallel self-loops
                // stack their chips on separate rows so they stay readable.
                // path[2].Y = box bottom - 10 - index*18 (see SelfLoopPath).
                float boxBottom = path[2].Y + 10f + edge.ParallelIndex * 18f;
                mid = new PointF(path[1].X, boxBottom + 10f + edge.ParallelIndex * 16f);
            }
            else
            {
                // Rail edges: the only node-free stretch is the vertical rail
                // run itself — hang the chip just right of its midpoint.
                // (Forward edges use rotated port labels instead of chips.)
                mid = new PointF(path[1].X + 6f, (path[1].Y + path[2].Y) / 2f);
            }

            var size = s.MeasureString(edge.Label, ErdStyle.EdgeLabelFont);
            float lx = mid.X - size.Width / 2f;
            float ly = mid.Y - size.Height / 2f;
            if (edge.IsBack && !edge.IsSelf) lx = mid.X; // left-align beside the rail

            labels.Add(new EdgeLabel
            {
                Text = edge.Label,
                Backing = new RectangleF(lx - 3f, ly - 1f, size.Width + 6f, size.Height + 2f),
                Color = labelColor
            });
        }
    }
}
