using System;
using System.Collections.Generic;
using System.Drawing;
using DataverseErdVisualizer.Models;
using DataverseErdVisualizer.Rendering;

namespace DataverseErdVisualizer.Layout
{
    /// <summary>
    /// Puts hand-placed tables back where the user left them after the diagram
    /// is rebuilt.
    ///
    /// Every rebuild — toggling labels, changing the column mode, ticking a
    /// table — produces a brand new graph from a fresh layout run, so without
    /// this the arrangement someone spent minutes on is thrown away by the next
    /// click. Positions are keyed by table logical name, which is what survives
    /// a rebuild; the node objects themselves do not.
    /// </summary>
    public static class PinnedLayout
    {
        /// <summary>
        /// Moves pinned tables to their stored positions and returns the canvas
        /// size needed to still contain everything. Tables that are no longer
        /// in the diagram are ignored, and their pins are kept for when they
        /// come back.
        /// </summary>
        public static SizeF Apply(ErdDiagram diagram, IDictionary<string, PointF> pinned)
        {
            if (diagram?.Graph == null || pinned == null || pinned.Count == 0)
                return diagram?.CanvasSize ?? SizeF.Empty;

            var moved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pin in pinned)
            {
                var node = diagram.Graph[pin.Key];
                if (node == null) continue;

                node.Bounds = new RectangleF(
                    Math.Max(0f, pin.Value.X), Math.Max(0f, pin.Value.Y),
                    node.Bounds.Width, node.Bounds.Height);
                node.Pinned = true;
                moved.Add(node.Id);
            }
            if (moved.Count == 0) return diagram.CanvasSize;

            // The routing was computed for the automatic positions, so anything
            // touching a moved box has to be re-routed. Clearing it drops those
            // edges back to the renderer's direct route, the same as dragging.
            foreach (var edge in diagram.Graph.Edges)
            {
                bool fromMoved = moved.Contains(edge.FromId);
                bool toMoved = moved.Contains(edge.ToId);
                if (!fromMoved && !toMoved) continue;

                edge.Route = null;
                edge.LaneY = null;
                edge.RailX = null;
                if (fromMoved) edge.FromPortX = null;
                if (toMoved) edge.ToPortX = null;
            }

            diagram.CanvasSize = Fit(diagram);
            return diagram.CanvasSize;
        }

        /// <summary>Grows the canvas so no table sits outside it.</summary>
        private static SizeF Fit(ErdDiagram diagram)
        {
            float width = diagram.CanvasSize.Width;
            float height = diagram.CanvasSize.Height;
            foreach (var node in diagram.Graph.Nodes)
            {
                width = Math.Max(width, node.Bounds.Right + ErdStyle.Margin);
                height = Math.Max(height, node.Bounds.Bottom + ErdStyle.Margin);
            }
            return new SizeF(width, height);
        }

        /// <summary>The hand-placed tables of a diagram, ready to store.</summary>
        public static Dictionary<string, PointF> Collect(ErdDiagram diagram)
        {
            var result = new Dictionary<string, PointF>(StringComparer.OrdinalIgnoreCase);
            if (diagram?.Graph == null) return result;

            foreach (var node in diagram.Graph.Nodes)
                if (node.Pinned)
                    result[node.Id] = node.Bounds.Location;
            return result;
        }
    }
}
