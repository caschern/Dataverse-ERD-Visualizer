using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DataverseErdVisualizer.Models;
using DataverseErdVisualizer.Rendering;

namespace DataverseErdVisualizer.UI
{
    /// <summary>
    /// A scrollable, zoomable panel that previews an <see cref="ErdDiagram"/>
    /// using the GDI+ surface. Ctrl+MouseWheel zooms, empty-space drag pans,
    /// dragging a box repositions the table (its edges re-route on the fly).
    /// </summary>
    public class ErdDiagramPanel : Panel
    {
        private ErdDiagram _diagram;
        private float _zoom = 1f;
        private bool _autoFit = true;   // re-fit on resize until the user zooms manually
        private bool _fitting;          // guards against resize/scrollbar feedback loops
        private string _selectedId;

        /// <summary>Raised when the user clicks a table (null when the selection is cleared).</summary>
        public event Action<ErdNode> NodeSelected;

        /// <summary>Raised when the user enters or leaves full-screen mode.</summary>
        public event Action<bool> FullScreenChanged;

        /// <summary>True while the pickers are hidden to give the diagram the full width.</summary>
        public bool IsFullScreen { get; private set; }

        public ErdDiagramPanel()
        {
            DoubleBuffered = true;
            AutoScroll = true;
            BackColor = Color.FromArgb(245, 246, 248);
            SetStyle(ControlStyles.Selectable, true); // needed for Esc to exit full screen
            TabStop = true;
        }

        public void ToggleFullScreen()
        {
            IsFullScreen = !IsFullScreen;
            FullScreenChanged?.Invoke(IsFullScreen);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape && IsFullScreen)
            {
                ToggleFullScreen();
                e.Handled = true;
            }
        }

        public float Zoom
        {
            get => _zoom;
            set
            {
                _zoom = value < 0.1f ? 0.1f : (value > 3f ? 3f : value);
                UpdateScrollSize();
                Invalidate();
            }
        }

        public void SetDiagram(ErdDiagram diagram)
        {
            _diagram = diagram;

            // Keep the selection when the same table survives a rebuild.
            if (_selectedId != null && diagram?.Graph[_selectedId] == null)
                _selectedId = null;
            NodeSelected?.Invoke(_selectedId == null ? null : diagram?.Graph[_selectedId]);

            AutoScrollPosition = new Point(0, 0);
            if (_autoFit) FitCore();
            else UpdateScrollSize();
            Invalidate();
        }

        public ErdDiagram Diagram => _diagram;

        public void ZoomToFit()
        {
            _autoFit = true;
            FitCore();
        }

        private void FitCore()
        {
            if (_diagram == null || _diagram.CanvasSize.Width < 1) { Zoom = 1f; return; }
            if (ClientSize.Width < 40 || ClientSize.Height < 40) return;

            _fitting = true;
            try
            {
                // Fit to width; the panel scrolls vertically for the rest.
                float fx = (ClientSize.Width - 20) / _diagram.CanvasSize.Width;
                Zoom = Math.Min(fx, 1f);
            }
            finally
            {
                _fitting = false;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_autoFit && !_fitting && _diagram != null)
                FitCore();
        }

        private void UpdateScrollSize()
        {
            if (_diagram == null) { AutoScrollMinSize = Size.Empty; return; }
            AutoScrollMinSize = new Size(
                (int)(_diagram.CanvasSize.Width * _zoom) + 20,
                (int)(_diagram.CanvasSize.Height * _zoom) + 20);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_diagram == null)
            {
                TextRenderer.DrawText(e.Graphics,
                    "Load solutions, pick one, and its ERD will appear here.",
                    Font, ClientRectangle, Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            var g = e.Graphics;
            g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
            g.ScaleTransform(_zoom, _zoom);

            g.FillRectangle(Brushes.White, 0, 0, _diagram.CanvasSize.Width, _diagram.CanvasSize.Height);

            using (var surface = new GdiDiagramSurface(g))
                ErdRenderer.Render(surface, _diagram.Graph, _diagram.CanvasSize,
                    interactive: true, highlightId: _selectedId);

            // Selection highlight, drawn in diagram coordinates on top.
            if (_selectedId != null)
            {
                var selected = _diagram.Graph[_selectedId];
                if (selected != null)
                {
                    var r = selected.Bounds;
                    r.Inflate(3f, 3f);
                    using (var pen = new Pen(Color.FromArgb(37, 118, 220), 2f))
                        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                }
            }
        }

        // Drag state: pressing empty space starts a pan, pressing a box starts a
        // node drag; selection happens on release when the mouse never moved.
        private bool _mouseDown;
        private bool _panned;
        private Point _dragStartClient;
        private Point _dragStartScroll;
        private ErdNode _dragNode;
        private PointF _dragNodeStart;

        /// <summary>Edge geometry captured at drag start, so moving a box
        /// translates its connections instead of degrading them.</summary>
        private class DragEdgeState
        {
            public float? FromPortX;
            public float? ToPortX;
            public List<PointF> Route;
        }
        private Dictionary<ErdEdge, DragEdgeState> _dragEdgeStates;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left || _diagram == null) return;

            _mouseDown = true;
            _panned = false;
            _dragStartClient = e.Location;
            _dragStartScroll = new Point(-AutoScrollPosition.X, -AutoScrollPosition.Y);

            _dragNode = HitTest(e.Location);
            if (_dragNode != null)
            {
                _dragNodeStart = _dragNode.Bounds.Location;

                _dragEdgeStates = new Dictionary<ErdEdge, DragEdgeState>();
                foreach (var edge in _diagram.Graph.Edges)
                {
                    if (edge.IsSelf) continue; // loops follow their box for free
                    if (!string.Equals(edge.FromId, _dragNode.Id, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(edge.ToId, _dragNode.Id, StringComparison.OrdinalIgnoreCase)) continue;
                    _dragEdgeStates[edge] = new DragEdgeState
                    {
                        FromPortX = edge.FromPortX,
                        ToPortX = edge.ToPortX,
                        Route = edge.Route == null ? null : new List<PointF>(edge.Route)
                    };
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_diagram == null) return;

            if (_mouseDown)
            {
                int dx = e.X - _dragStartClient.X;
                int dy = e.Y - _dragStartClient.Y;
                if (!_panned && (Math.Abs(dx) > 3 || Math.Abs(dy) > 3))
                {
                    _panned = true;
                    Cursor = _dragNode != null ? Cursors.SizeAll : Cursors.Hand;
                }
                if (_panned)
                {
                    if (_dragNode != null)
                        MoveNode(_dragNode, dx / _zoom, dy / _zoom);
                    else
                        AutoScrollPosition = new Point(_dragStartScroll.X - dx, _dragStartScroll.Y - dy);
                }
                return;
            }

            Cursor = HitTest(e.Location) != null ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !_mouseDown) return;

            _mouseDown = false;
            var wasNodeDrag = _panned && _dragNode != null;
            _dragNode = null;
            _dragEdgeStates = null;
            if (_panned)
            {
                _panned = false;
                Cursor = Cursors.Default;
                if (wasNodeDrag) Invalidate();
                return; // it was a drag, not a click
            }

            var node = HitTest(e.Location);
            _selectedId = node?.Id;
            Invalidate();
            NodeSelected?.Invoke(node);
        }

        /// <summary>
        /// Repositions a dragged box, translating its ports and route endpoints
        /// with it. Lanes and channels survive the move, so the connections
        /// stay fanned out instead of bunching at the box center.
        /// </summary>
        private void MoveNode(ErdNode node, float dx, float dy)
        {
            node.Bounds = new RectangleF(
                _dragNodeStart.X + dx, _dragNodeStart.Y + dy,
                node.Bounds.Width, node.Bounds.Height);
            node.Pinned = true;

            if (_dragEdgeStates != null)
            {
                foreach (var kv in _dragEdgeStates)
                {
                    var edge = kv.Key;
                    var orig = kv.Value;
                    bool fromHere = string.Equals(edge.FromId, node.Id, StringComparison.OrdinalIgnoreCase);
                    bool toHere = string.Equals(edge.ToId, node.Id, StringComparison.OrdinalIgnoreCase);

                    if (fromHere && orig.FromPortX.HasValue) edge.FromPortX = orig.FromPortX.Value + dx;
                    if (toHere && orig.ToPortX.HasValue) edge.ToPortX = orig.ToPortX.Value + dx;

                    if (orig.Route == null) continue;
                    if (orig.Route.Count <= 2)
                    {
                        // A straight multi-rank drop turns diagonal when its box
                        // moves sideways; let the renderer re-route it instead.
                        edge.Route = null;
                        continue;
                    }

                    // Shift the endpoint and its first bend; the reserved
                    // channel through the middle stays where it was routed.
                    var pts = new List<PointF>(orig.Route);
                    if (fromHere)
                    {
                        float x = orig.Route[0].X + dx;
                        pts[0] = new PointF(x, node.Bounds.Bottom);
                        pts[1] = new PointF(x, pts[1].Y);
                    }
                    if (toHere)
                    {
                        int n = pts.Count;
                        float x = orig.Route[n - 1].X + dx;
                        pts[n - 1] = new PointF(x, node.Bounds.Y);
                        pts[n - 2] = new PointF(x, pts[n - 2].Y);
                    }
                    edge.Route = pts;
                }
            }

            // Grow the canvas when a box is dragged past its edge.
            var size = _diagram.CanvasSize;
            bool grew = false;
            if (node.Bounds.Right + 30f > size.Width) { size.Width = node.Bounds.Right + 30f; grew = true; }
            if (node.Bounds.Bottom + 30f > size.Height) { size.Height = node.Bounds.Bottom + 30f; grew = true; }
            if (grew)
            {
                _diagram.CanvasSize = size;
                UpdateScrollSize();
            }

            Invalidate();
        }

        private PointF ToDiagram(Point client)
        {
            if (_zoom <= 0f) return PointF.Empty;
            return new PointF(
                (client.X - AutoScrollPosition.X) / _zoom,
                (client.Y - AutoScrollPosition.Y) / _zoom);
        }

        /// <summary>Maps a client point back through scroll + zoom to diagram space.</summary>
        private ErdNode HitTest(Point client)
        {
            if (_diagram == null || _zoom <= 0f) return null;
            var pt = ToDiagram(client);

            // Topmost node wins (nodes are drawn in list order).
            for (int i = _diagram.Graph.Nodes.Count - 1; i >= 0; i--)
            {
                var n = _diagram.Graph.Nodes[i];
                if (n.Bounds.Contains(pt.X, pt.Y)) return n;
            }
            return null;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                ZoomAt(e.Delta > 0 ? 1.1f : 1f / 1.1f, e.Location);
                ((HandledMouseEventArgs)e).Handled = true;
            }
            else
            {
                base.OnMouseWheel(e);
            }
        }

        /// <summary>Zooms by a factor keeping the given client point stationary.</summary>
        private void ZoomAt(float factor, Point clientAnchor)
        {
            _autoFit = false; // manual zoom takes over until the next Zoom to Fit
            var anchor = ToDiagram(clientAnchor);
            Zoom = _zoom * factor;
            AutoScrollPosition = new Point(
                (int)(anchor.X * _zoom - clientAnchor.X),
                (int)(anchor.Y * _zoom - clientAnchor.Y));
            Invalidate();
        }

        /// <summary>Zoom in/out anchored at the viewport center (for the +/- buttons).</summary>
        public void ZoomStep(float factor)
            => ZoomAt(factor, new Point(ClientSize.Width / 2, ClientSize.Height / 2));

        /// <summary>
        /// Selects and centers the next table whose display or logical name
        /// contains the term, cycling through matches. False when none match.
        /// </summary>
        public bool FindNext(string term)
        {
            if (_diagram == null || string.IsNullOrWhiteSpace(term)) return false;
            term = term.Trim();

            var matches = _diagram.Graph.Nodes
                .Where(n => (n.Title ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            (n.Id ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            if (matches.Count == 0) return false;

            int start = 0;
            if (_selectedId != null)
            {
                int current = matches.FindIndex(n =>
                    string.Equals(n.Id, _selectedId, StringComparison.OrdinalIgnoreCase));
                if (current >= 0) start = (current + 1) % matches.Count;
            }
            var target = matches[start];

            _selectedId = target.Id;
            NodeSelected?.Invoke(target);
            CenterOn(target);
            Invalidate();
            return true;
        }

        private void CenterOn(ErdNode node)
        {
            float cx = (node.Bounds.X + node.Bounds.Width / 2f) * _zoom;
            float cy = (node.Bounds.Y + node.Bounds.Height / 2f) * _zoom;
            AutoScrollPosition = new Point(
                (int)(cx - ClientSize.Width / 2f),
                (int)(cy - ClientSize.Height / 2f));
        }
    }
}
