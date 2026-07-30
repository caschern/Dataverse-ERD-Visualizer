using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DataverseErdVisualizer.Models;

namespace DataverseErdVisualizer.UI
{
    /// <summary>
    /// Right-hand pane showing everything about the selected table: identity,
    /// all columns (not just the rows the box displays) and its relationships.
    /// </summary>
    public class EntityDetailsPane : Panel
    {
        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly ListView _list;

        public EntityDetailsPane()
        {
            BackColor = Color.White;
            Padding = new Padding(0);

            _title = new Label
            {
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 41, 54),
                Padding = new Padding(8, 8, 8, 0),
                AutoSize = false,
                Height = 30,
                Text = "No table selected"
            };
            _subtitle = new Label
            {
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(110, 118, 129),
                Padding = new Padding(8, 0, 8, 6),
                AutoSize = false,
                Height = 22,
                Text = "Click a table in the diagram"
            };

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BorderStyle = BorderStyle.None,
                ShowGroups = true
            };
            _list.Columns.Add("Name", 140);
            _list.Columns.Add("Detail", 150);
            _list.Resize += (s, e) => AutoSizeColumns();

            Controls.Add(_list);
            Controls.Add(_subtitle);
            Controls.Add(_title);
        }

        private void AutoSizeColumns()
        {
            int w = _list.ClientSize.Width;
            if (w < 60) return;
            _list.Columns[0].Width = (int)(w * 0.48);
            _list.Columns[1].Width = w - _list.Columns[0].Width - 4;
        }

        /// <summary>Shows the given table (null clears the pane). The graph supplies its relationships.</summary>
        public void SetNode(ErdNode node, ErdGraph graph)
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            _list.Groups.Clear();

            if (node?.Entity == null)
            {
                _title.Text = "No table selected";
                _subtitle.Text = "Click a table in the diagram";
                _list.EndUpdate();
                return;
            }

            var entity = node.Entity;
            _title.Text = entity.DisplayName ?? entity.LogicalName;
            _subtitle.Text = entity.LogicalName +
                (entity.IsExternal ? " · outside this solution" : "");

            var infoGroup = new ListViewGroup("Table", HorizontalAlignment.Left);
            var colGroup = new ListViewGroup($"Columns ({entity.Attributes.Count})", HorizontalAlignment.Left);
            var relGroup = new ListViewGroup("Relationships", HorizontalAlignment.Left);
            _list.Groups.Add(infoGroup);
            _list.Groups.Add(colGroup);
            _list.Groups.Add(relGroup);

            void Add(ListViewGroup group, string name, string detail)
            {
                var item = new ListViewItem(name) { Group = group };
                item.SubItems.Add(detail ?? "");
                _list.Items.Add(item);
            }

            Add(infoGroup, "Schema name", entity.SchemaName ?? entity.LogicalName);
            if (!string.IsNullOrEmpty(entity.OwnershipType)) Add(infoGroup, "Ownership", entity.OwnershipType);
            Add(infoGroup, "Type", entity.IsActivity ? "Activity" : entity.IsCustom ? "Custom" : "Standard");
            if (!string.IsNullOrEmpty(entity.Description)) Add(infoGroup, "Description", entity.Description);

            foreach (var attr in entity.Attributes
                .OrderBy(a => a.IsPrimaryId ? 0 : a.IsPrimaryName ? 1 : a.IsLookup ? 2 : 3)
                .ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                string marker = attr.IsPrimaryId ? "PK · " : attr.IsPrimaryName ? "Name · " : "";
                Add(colGroup, attr.DisplayName ?? attr.LogicalName, marker + attr.TypeLabel);
            }

            if (graph != null)
            {
                foreach (var edge in graph.Edges.Where(e =>
                    string.Equals(e.FromId, node.Id, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(e.ToId, node.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    var rel = edge.Relationship;
                    if (rel == null) continue;
                    string detail;
                    if (rel.Kind == RelationshipKind.ManyToMany)
                        detail = "N:N " + rel.ReferencedEntity + " ↔ " + rel.ReferencingEntity;
                    else if (string.Equals(edge.FromId, node.Id, StringComparison.OrdinalIgnoreCase))
                        detail = "1:N → " + rel.ReferencingEntity + " (" + (rel.LookupDisplayName ?? rel.LookupAttribute) + ")";
                    else
                        detail = "N:1 → " + rel.ReferencedEntity + " (" + (rel.LookupDisplayName ?? rel.LookupAttribute) + ")";
                    Add(relGroup, rel.SchemaName, detail);
                }
            }

            _list.EndUpdate();
            AutoSizeColumns();
        }
    }
}
