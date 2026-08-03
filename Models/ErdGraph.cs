using System.Collections.Generic;
using System.Drawing;

namespace DataverseErdVisualizer.Models
{
    /// <summary>Visual flavor of an entity box (drives the header color).</summary>
    public enum NodeFlavor
    {
        Standard,
        Custom,
        Activity,
        External
    }

    /// <summary>Badge shown in front of an attribute row.</summary>
    public enum RowBadge
    {
        None,
        PrimaryKey,
        PrimaryName,
        Lookup
    }

    /// <summary>One attribute line inside an entity box.</summary>
    public class ErdRow
    {
        public RowBadge Badge { get; set; }
        public string Name { get; set; }

        /// <summary>Right-aligned grey type text ("Text", "Lookup(account)").</summary>
        public string Type { get; set; }

        // --- assigned by the sizing pass (possibly ellipsis-truncated) ---
        public string DisplayName { get; set; }
        public string DisplayType { get; set; }
    }

    /// <summary>An entity box in the diagram.</summary>
    public class ErdNode
    {
        /// <summary>Entity logical name (unique per diagram).</summary>
        public string Id { get; set; }

        /// <summary>Header line: entity display name.</summary>
        public string Title { get; set; }

        /// <summary>Header second line: logical name.</summary>
        public string Subtitle { get; set; }

        public NodeFlavor Flavor { get; set; }
        public List<ErdRow> Rows { get; } = new List<ErdRow>();

        /// <summary>Attributes hidden by the display cap ("+ n more").</summary>
        public int MoreCount { get; set; }

        /// <summary>The source table, for the details pane (null for layout virtuals).</summary>
        public EntityModel Entity { get; set; }

        // --- assigned by the layout engine ---
        public int Rank { get; set; } = -1;
        public RectangleF Bounds { get; set; }

        /// <summary>Height of the colored header band (assigned by the sizer).</summary>
        public float HeaderHeight { get; set; }

        /// <summary>True when the user dragged this box; layout leaves it in place on refresh.</summary>
        public bool Pinned { get; set; }
    }

    /// <summary>A relationship line between two entity boxes.</summary>
    public class ErdEdge
    {
        /// <summary>The "one" side (referenced/parent). Entity1 for N:N.</summary>
        public string FromId { get; set; }

        /// <summary>The "many" side (referencing/child). Entity2 for N:N.</summary>
        public string ToId { get; set; }

        public RelationshipKind Kind { get; set; }

        /// <summary>Chip text (lookup column display name, or intersect table for N:N).</summary>
        public string Label { get; set; }

        /// <summary>The source relationship, for the details pane.</summary>
        public RelationshipModel Relationship { get; set; }

        public bool IsSelf { get; set; }

        // --- parallel-edge fanning (same unordered entity pair) ---
        public int ParallelIndex { get; set; }
        public int ParallelCount { get; set; } = 1;

        // --- assigned by the layout engine's port pass ---

        /// <summary>Absolute X where this edge leaves the bottom of its From box (null = center fallback).</summary>
        public float? FromPortX { get; set; }

        /// <summary>Absolute X where this edge enters the top of its To box (null = center fallback).</summary>
        public float? ToPortX { get; set; }

        /// <summary>
        /// Label rides the connector's FIRST segment instead of its last. Set
        /// for satellite-cluster edges whose polyline starts at the satellite,
        /// where the shared hub end would pile every label onto one point.
        /// </summary>
        public bool LabelAtSource { get; set; }

        /// <summary>
        /// Not drawn on the diagram — a parallel relationship folded into a
        /// sibling connector's "xN" marker. Still listed in the details pane
        /// and in every export, so no relationship is ever lost.
        /// </summary>
        public bool Hidden { get; set; }

        /// <summary>
        /// When &gt; 1, this connector stands in for that many parallel
        /// relationships between the same pair and is marked "xN".
        /// </summary>
        public int CollapsedCount { get; set; }

        // --- assigned by the layout engine's routing pass ---
        public bool IsBack { get; set; }
        public float? LaneY { get; set; }
        public float? RailX { get; set; }
        public List<PointF> Route { get; set; }
    }

    public class ErdGraph
    {
        public string Title { get; set; }
        public string Subtitle { get; set; }

        /// <summary>
        /// Extra vertical space between ranks (set by the builder when edge
        /// labels are shown, so the rotated labels have room to breathe).
        /// </summary>
        public float ExtraRankGap { get; set; }

        /// <summary>
        /// Distance between neighboring ports on a box border. Must exceed the
        /// rotated label column width (~16px) when edge labels are shown, or a
        /// label's backing erases the next port's line.
        /// </summary>
        public float PortSpacing { get; set; } = 14f;

        public List<ErdNode> Nodes { get; } = new List<ErdNode>();
        public List<ErdEdge> Edges { get; } = new List<ErdEdge>();

        private readonly Dictionary<string, ErdNode> _byId =
            new Dictionary<string, ErdNode>(System.StringComparer.OrdinalIgnoreCase);

        public ErdNode AddNode(ErdNode node)
        {
            Nodes.Add(node);
            _byId[node.Id] = node;
            return node;
        }

        public ErdEdge AddEdge(ErdEdge edge)
        {
            Edges.Add(edge);
            return edge;
        }

        public ErdNode this[string id]
        {
            get
            {
                ErdNode n;
                return id != null && _byId.TryGetValue(id, out n) ? n : null;
            }
        }

        public bool Contains(string id) => id != null && _byId.ContainsKey(id);
    }

    /// <summary>A laid-out diagram ready to render.</summary>
    public class ErdDiagram
    {
        public ErdGraph Graph { get; set; }
        public SizeF CanvasSize { get; set; }
    }
}
