using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DataverseErdVisualizer.Data;
using DataverseErdVisualizer.Exporters;
using DataverseErdVisualizer.Models;
using DataverseErdVisualizer.Rendering;
using DataverseErdVisualizer.UI;
using XrmToolBox.Extensibility;

namespace DataverseErdVisualizer
{
    public partial class ErdVisualizerControl : PluginControlBase
    {
        // When a solution has more tables than this, start with none checked so
        // the first render doesn't try to draw the whole Default solution.
        private const int LargeSolutionThreshold = 100;

        private ToolStripButton _loadButton;
        private ToolStripDropDownButton _columnsDrop;
        private ToolStripDropDownButton _optionsDrop;
        private ToolStripDropDownButton _exportDrop;
        private ToolStripButton _fitButton;
        private ToolStripTextBox _findBox;
        private ToolStripLabel _status;

        private ListView _solutionList;
        private TextBox _solutionSearch;
        private CheckedListBox _entityList;
        private TextBox _entitySearch;
        private LinkLabel _checkAll;
        private LinkLabel _checkNone;
        private ErdDiagramPanel _panel;
        private EntityDetailsPane _details;
        private SplitContainer _outerSplit;

        private List<SolutionInfo> _allSolutions = new List<SolutionInfo>();
        private ErdModel _model;
        private readonly ErdOptions _options = new ErdOptions();
        private readonly Timer _rebuildDebounce;
        private bool _suspendEntityEvents;

        public ErdVisualizerControl()
        {
            _rebuildDebounce = new Timer { Interval = 300 };
            _rebuildDebounce.Tick += (s, e) => { _rebuildDebounce.Stop(); Rebuild(); };
            BuildUi();
        }

        // ---------------------------------------------------------------- UI

        private void BuildUi()
        {
            Dock = DockStyle.Fill;

            var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };

            _loadButton = new ToolStripButton("Load Solutions")
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text
            };
            _loadButton.Click += (s, e) => ExecuteMethod(LoadSolutions);

            _columnsDrop = BuildColumnsDropDown();
            _optionsDrop = BuildOptionsDropDown();
            _exportDrop = BuildExportDropDown();

            _fitButton = new ToolStripButton("Zoom to Fit")
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Enabled = false
            };
            _fitButton.Click += (s, e) => _panel.ZoomToFit();

            _findBox = new ToolStripTextBox { Width = 160, ToolTipText = "Find a table by name (Enter = next match)" };
            _findBox.TextBox.HandleCreated += (s, e) =>
                SendMessage(_findBox.TextBox.Handle, EM_SETCUEBANNER, (IntPtr)1, "Find table…");
            _findBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    if (!_panel.FindNext(_findBox.Text))
                        System.Media.SystemSounds.Asterisk.Play();
                }
            };

            var closeButton = new ToolStripButton("Close")
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Alignment = ToolStripItemAlignment.Right
            };
            closeButton.Click += (s, e) => CloseTool();

            _status = new ToolStripLabel("Not loaded") { ForeColor = Color.Gray };

            toolbar.Items.AddRange(new ToolStripItem[]
            {
                _loadButton, new ToolStripSeparator(),
                _columnsDrop, _optionsDrop, new ToolStripSeparator(),
                _exportDrop, new ToolStripSeparator(),
                _fitButton, _findBox, new ToolStripSeparator(),
                _status, closeButton
            });

            // ---- left column: solutions above, table checklist below ----
            _solutionList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false
            };
            _solutionList.Columns.Add("Solution", 190);
            _solutionList.Columns.Add("Version", 70);
            _solutionList.Columns.Add("Managed", 62);
            _solutionList.Columns.Add("Publisher", 120);
            _solutionList.SelectedIndexChanged += (s, e) => OnSolutionSelected();

            _solutionSearch = CreateSearchBox("Filter solutions…");
            _solutionSearch.TextChanged += (s, e) => FillSolutionList();

            _entityList = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                IntegralHeight = false,
                BorderStyle = BorderStyle.None
            };
            _entityList.ItemCheck += (s, e) =>
            {
                if (_suspendEntityEvents) return;
                _rebuildDebounce.Stop();
                _rebuildDebounce.Start();
            };

            _entitySearch = CreateSearchBox("Filter tables…");
            _entitySearch.TextChanged += (s, e) => FillEntityList();

            _checkAll = new LinkLabel { Text = "All", AutoSize = true, Margin = new Padding(0) };
            _checkNone = new LinkLabel { Text = "None", AutoSize = true };
            _checkAll.LinkClicked += (s, e) => SetAllChecked(true);
            _checkNone.LinkClicked += (s, e) => SetAllChecked(false);

            var linkRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 22,
                Padding = new Padding(4, 3, 0, 0),
                BackColor = Color.FromArgb(238, 241, 245)
            };
            var tablesLabel = new Label { Text = "Tables:", AutoSize = true, ForeColor = Color.DimGray };
            linkRow.Controls.Add(tablesLabel);
            linkRow.Controls.Add(_checkAll);
            linkRow.Controls.Add(_checkNone);

            var entityHost = new Panel { Dock = DockStyle.Fill };
            entityHost.Controls.Add(_entityList);
            entityHost.Controls.Add(_entitySearch);
            entityHost.Controls.Add(linkRow);
            _entityList.BringToFront();

            var solutionHost = new Panel { Dock = DockStyle.Fill };
            solutionHost.Controls.Add(_solutionList);
            solutionHost.Controls.Add(_solutionSearch);
            _solutionList.BringToFront();

            var leftSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 220
            };
            leftSplit.Panel1.Controls.Add(solutionHost);
            leftSplit.Panel2.Controls.Add(entityHost);

            // ---- right side: diagram + details ----
            _panel = new ErdDiagramPanel { Dock = DockStyle.Fill };
            _details = new EntityDetailsPane();
            _panel.NodeSelected += n => _details.SetNode(n, _panel.Diagram?.Graph);
            _panel.FullScreenChanged += full => _outerSplit.Panel1Collapsed = full;

            _outerSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 300
            };
            _outerSplit.Panel1.Controls.Add(leftSplit);
            _outerSplit.Panel2.Controls.Add(WrapWithDetails(_panel, _details));

            Controls.Add(_outerSplit);
            Controls.Add(toolbar);
        }

        private ToolStripDropDownButton BuildColumnsDropDown()
        {
            var drop = new ToolStripDropDownButton("Columns: Keys && lookups")
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text
            };

            void AddMode(string text, AttributeDisplayMode mode)
            {
                var item = new ToolStripMenuItem(text) { Checked = mode == _options.AttributeMode, Tag = mode };
                item.Click += (s, e) =>
                {
                    _options.AttributeMode = mode;
                    foreach (ToolStripMenuItem other in drop.DropDownItems)
                        other.Checked = Equals(other.Tag, mode);
                    drop.Text = "Columns: " + text.Replace("&", "&&");
                    Rebuild();
                };
                drop.DropDownItems.Add(item);
            }

            AddMode("Keys & lookups", AttributeDisplayMode.KeysAndLookups);
            AddMode("Custom only", AttributeDisplayMode.CustomOnly);
            AddMode("All", AttributeDisplayMode.All);
            AddMode("None (boxes only)", AttributeDisplayMode.None);
            return drop;
        }

        private ToolStripDropDownButton BuildOptionsDropDown()
        {
            var drop = new ToolStripDropDownButton("Options")
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text
            };

            ToolStripMenuItem Toggle(string text, bool initial, Action<bool> apply)
            {
                var item = new ToolStripMenuItem(text) { Checked = initial, CheckOnClick = true };
                item.CheckedChanged += (s, e) => { apply(item.Checked); Rebuild(); };
                drop.DropDownItems.Add(item);
                return item;
            }

            var cluster = Toggle("Group satellite tables", _options.ClusterSatelliteTables,
                v => _options.ClusterSatelliteTables = v);
            cluster.ToolTipText = "Pack tables that relate only to one hub into a compact grid " +
                                  "beside it, instead of one very wide row.";

            var allSatelliteEdges = Toggle("   …show every satellite relationship",
                _options.ShowAllSatelliteRelationships,
                v => _options.ShowAllSatelliteRelationships = v);
            allSatelliteEdges.ToolTipText =
                "A satellite with several lookups to the same hub shows one connector marked " +
                "\"x3\" by default. Tick this to draw each relationship separately.";
            drop.DropDownItems.Add(new ToolStripSeparator());

            Toggle("N:N relationships", _options.IncludeManyToMany, v => _options.IncludeManyToMany = v);
            Toggle("Self-referential loops", _options.IncludeSelfReferential, v => _options.IncludeSelfReferential = v);
            Toggle("Related external tables", _options.IncludeExternalEntities, v => _options.IncludeExternalEntities = v);
            Toggle("Relationship labels", _options.ShowEdgeLabels, v => _options.ShowEdgeLabels = v);
            drop.DropDownItems.Add(new ToolStripSeparator());
            Toggle("System columns && relationships", _options.IncludeSystemRelationships,
                v => _options.IncludeSystemRelationships = v);
            return drop;
        }

        private ToolStripDropDownButton BuildExportDropDown()
        {
            var drop = new ToolStripDropDownButton("Export")
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Enabled = false
            };

            void Add(string text, string filter, string extension, Action<ErdDiagram, string> save)
            {
                var item = new ToolStripMenuItem(text);
                item.Click += (s, e) => Export(filter, extension, save);
                drop.DropDownItems.Add(item);
            }

            Add("PNG image…", "PNG image (*.png)|*.png", ".png", (d, p) => PngExporter.Save(d, p));
            Add("SVG vector…", "SVG image (*.svg)|*.svg", ".svg", SvgExporter.Save);
            Add("PDF document…", "PDF document (*.pdf)|*.pdf", ".pdf", PdfExporter.Save);
            Add("HTML data dictionary…", "HTML document (*.html)|*.html", ".html", HtmlExporter.Save);
            Add("Mermaid erDiagram…", "Mermaid/Markdown (*.mmd)|*.mmd|Text file (*.txt)|*.txt", ".mmd", MermaidExporter.Save);
            drop.DropDownItems.Add(new ToolStripSeparator());

            var kb = new ToolStripMenuItem("Knowledge base for AI agents (Markdown)…")
            {
                ToolTipText = "A retrieval-shaped data dictionary for grounding a Copilot Studio " +
                              "agent: one section per table, relationships written out from both " +
                              "sides, no diagram embedded."
            };
            kb.Click += (s, e) => Export("Markdown (*.md)|*.md|Text file (*.txt)|*.txt", ".md",
                MarkdownExporter.Save);
            drop.DropDownItems.Add(kb);
            return drop;
        }

        private static Control WrapWithDetails(ErdDiagramPanel diagram, EntityDetailsPane details)
        {
            var host = new Panel { Dock = DockStyle.Fill };
            details.Dock = DockStyle.Right;
            details.Width = 280;
            var splitter = new Splitter { Dock = DockStyle.Right, Width = 5 };

            // Overlay buttons pinned to the top-right corner of the map area.
            var mapArea = new Panel { Dock = DockStyle.Fill };

            Button OverlayButton(string text)
            {
                var b = new Button
                {
                    Text = text,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.White,
                    UseVisualStyleBackColor = false,
                    TabStop = false
                };
                b.FlatAppearance.BorderColor = Color.FromArgb(200, 205, 212);
                return b;
            }

            var fullScreenButton = OverlayButton("Full screen");
            fullScreenButton.Click += (s, e) => diagram.ToggleFullScreen();
            diagram.FullScreenChanged += full =>
                fullScreenButton.Text = full ? "Exit full screen" : "Full screen";

            var zoomInButton = OverlayButton("+");
            zoomInButton.Click += (s, e) => diagram.ZoomStep(1.2f);
            var zoomOutButton = OverlayButton("−");
            zoomOutButton.Click += (s, e) => diagram.ZoomStep(1f / 1.2f);

            void PositionButtons()
            {
                int right = mapArea.ClientSize.Width - 8 - SystemInformation.VerticalScrollBarWidth;
                fullScreenButton.Location = new Point(right - fullScreenButton.Width, 8);
                zoomInButton.Location = new Point(fullScreenButton.Left - zoomInButton.Width - 6, 8);
                zoomOutButton.Location = new Point(zoomInButton.Left - zoomOutButton.Width - 2, 8);
            }
            mapArea.Resize += (s, e) => PositionButtons();
            fullScreenButton.SizeChanged += (s, e) => PositionButtons();

            mapArea.Controls.Add(fullScreenButton);
            mapArea.Controls.Add(zoomInButton);
            mapArea.Controls.Add(zoomOutButton);
            mapArea.Controls.Add(diagram);
            fullScreenButton.BringToFront();
            zoomInButton.BringToFront();
            zoomOutButton.BringToFront();
            PositionButtons();

            host.Controls.Add(mapArea);
            host.Controls.Add(splitter);
            host.Controls.Add(details);
            return host;
        }

        private const int EM_SETCUEBANNER = 0x1501;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private static TextBox CreateSearchBox(string hint)
        {
            var box = new TextBox { Dock = DockStyle.Top };
            box.HandleCreated += (s, e) =>
                SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, hint);
            box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    box.Clear();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
            return box;
        }

        // ----------------------------------------------------------- loading

        private void LoadSolutions()
        {
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading solutions…",
                Work = (worker, args) =>
                {
                    args.Result = SolutionRepository.RetrieveSolutions(Service);
                },
                PostWorkCallBack = args =>
                {
                    if (args.Error != null)
                    {
                        MessageBox.Show(this, args.Error.Message, "Load failed",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    _allSolutions = (List<SolutionInfo>)args.Result;
                    FillSolutionList();
                    _status.Text = _allSolutions.Count + " solutions";
                }
            });
        }

        private void FillSolutionList()
        {
            var term = (_solutionSearch.Text ?? "").Trim();
            var items = string.IsNullOrEmpty(term)
                ? _allSolutions
                : _allSolutions.Where(s =>
                    (s.FriendlyName ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (s.UniqueName ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            _solutionList.BeginUpdate();
            _solutionList.Items.Clear();
            foreach (var sol in items)
            {
                var lvi = new ListViewItem(sol.FriendlyName ?? sol.UniqueName) { Tag = sol };
                lvi.SubItems.Add(sol.Version ?? "");
                lvi.SubItems.Add(sol.IsManaged ? "Yes" : "No");
                lvi.SubItems.Add(sol.Publisher ?? "");
                _solutionList.Items.Add(lvi);
            }
            _solutionList.EndUpdate();
        }

        private void OnSolutionSelected()
        {
            var solution = _solutionList.SelectedItems.Count > 0
                ? _solutionList.SelectedItems[0].Tag as SolutionInfo
                : null;
            if (solution == null) return;
            ExecuteMethod(() => LoadModel(solution));
        }

        private void LoadModel(SolutionInfo solution)
        {
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading metadata for " + solution.FriendlyName + "…",
                Work = (worker, args) =>
                {
                    args.Result = MetadataRepository.RetrieveModel(Service, solution,
                        s => worker.ReportProgress(0, s));
                },
                ProgressChanged = args => SetWorkingMessage(args.UserState?.ToString()),
                PostWorkCallBack = args =>
                {
                    if (args.Error != null)
                    {
                        MessageBox.Show(this, args.Error.Message, "Metadata load failed",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    _model = (ErdModel)args.Result;
                    PopulateEntityList();
                    Rebuild();
                }
            });
        }

        private class EntityListEntry
        {
            public EntityModel Entity;
            public override string ToString()
                => (Entity.DisplayName ?? Entity.LogicalName) + "  (" + Entity.LogicalName + ")";
        }

        private void PopulateEntityList()
        {
            _checkedByName.Clear();
            bool checkAll = SolutionTables().Count() <= LargeSolutionThreshold;
            foreach (var entity in SolutionTables())
                _checkedByName[entity.LogicalName] = checkAll;
            _entitySearch.Clear();
            FillEntityList();

            if (!checkAll)
                _status.Text = "Large solution — tick the tables to draw";
        }

        private IEnumerable<EntityModel> SolutionTables()
            => _model == null
                ? Enumerable.Empty<EntityModel>()
                : _model.Entities.Where(e => !e.IsIntersect && !e.IsExternal);

        // The checklist is search-filterable, so checked state lives here, not in the control.
        private readonly Dictionary<string, bool> _checkedByName =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private void FillEntityList()
        {
            CaptureChecklist();

            var term = (_entitySearch.Text ?? "").Trim();
            _suspendEntityEvents = true;
            _entityList.BeginUpdate();
            _entityList.Items.Clear();
            foreach (var entity in SolutionTables()
                .OrderBy(e => e.DisplayName ?? e.LogicalName, StringComparer.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(term) &&
                    (entity.DisplayName ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0 &&
                    entity.LogicalName.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                bool isChecked;
                if (!_checkedByName.TryGetValue(entity.LogicalName, out isChecked)) isChecked = true;
                _entityList.Items.Add(new EntityListEntry { Entity = entity }, isChecked);
            }
            _entityList.EndUpdate();
            _suspendEntityEvents = false;
        }

        /// <summary>Persists the visible checkboxes into the name→checked map.</summary>
        private void CaptureChecklist()
        {
            for (int i = 0; i < _entityList.Items.Count; i++)
            {
                var entry = (EntityListEntry)_entityList.Items[i];
                _checkedByName[entry.Entity.LogicalName] = _entityList.GetItemChecked(i);
            }
        }

        private void SetAllChecked(bool value)
        {
            _suspendEntityEvents = true;
            for (int i = 0; i < _entityList.Items.Count; i++)
                _entityList.SetItemChecked(i, value);
            _suspendEntityEvents = false;

            // "All"/"None" apply to the *visible* (filtered) rows only.
            CaptureChecklist();
            Rebuild();
        }

        // ---------------------------------------------------------- building

        private void Rebuild()
        {
            if (_model == null) return;

            CaptureChecklist();
            var selected = new HashSet<string>(
                _checkedByName.Where(kv => kv.Value).Select(kv => kv.Key),
                StringComparer.OrdinalIgnoreCase);

            _options.SelectedEntities = selected;

            try
            {
                Cursor = Cursors.WaitCursor;
                ErdDiagram diagram;
                using (var bmp = new Bitmap(1, 1))
                using (var g = Graphics.FromImage(bmp))
                using (var measure = new GdiDiagramSurface(g))
                {
                    diagram = ErdGraphBuilder.Build(_model, _options, measure);
                }
                _panel.SetDiagram(diagram);
                _panel.ZoomToFit();

                int tables = diagram.Graph.Nodes.Count;
                int rels = diagram.Graph.Edges.Count;
                _status.ForeColor = Color.DimGray;
                _status.Text = tables + " tables · " + rels + " relationships";
                UpdateButtons();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not render the diagram:\n\n" + ex.Message,
                    "Render error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void UpdateButtons()
        {
            bool hasDiagram = _panel.Diagram != null && _panel.Diagram.Graph.Nodes.Count > 0;
            _exportDrop.Enabled = hasDiagram;
            _fitButton.Enabled = hasDiagram;
        }

        // ---------------------------------------------------------- exporting

        private void Export(string filter, string extension, Action<ErdDiagram, string> save)
        {
            var diagram = _panel.Diagram;
            if (diagram == null || diagram.Graph.Nodes.Count == 0)
            {
                MessageBox.Show(this, "Generate a diagram first.", "Nothing to export",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = filter;
                var suffix = extension == ".md" ? "-data-model" : "-erd";
                dialog.FileName = MakeSafeFileName((diagram.Graph.Title ?? "erd") + suffix) + extension;

                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    Cursor = Cursors.WaitCursor;
                    save(diagram, dialog.FileName);

                    if (MessageBox.Show(this, "Export complete. Open the file now?", "Done",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        System.Diagnostics.Process.Start(dialog.FileName);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Export failed:\n\n" + ex.Message, "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            }
        }

        private static string MakeSafeFileName(string name)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Length > 80 ? name.Substring(0, 80) : name;
        }
    }
}
