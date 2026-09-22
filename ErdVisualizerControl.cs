using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using DataverseErdVisualizer.Compare;
using DataverseErdVisualizer.Data;
using DataverseErdVisualizer.Exporters;
using DataverseErdVisualizer.Layout;
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
        private ToolStripDropDownButton _compareDrop;
        private ToolStripButton _fitButton;
        private ToolStripButton _clearFocusButton;
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

        /// <summary>
        /// Hand-placed table positions for the loaded solution, keyed by logical
        /// name. Held here rather than on the diagram because every rebuild
        /// creates a new graph.
        /// </summary>
        private Dictionary<string, PointF> _pinned = new Dictionary<string, PointF>(StringComparer.OrdinalIgnoreCase);
        private string _solutionKey;

        /// <summary>
        /// While focused, the diagram shows one table's neighbourhood instead of
        /// the ticked selection. A lens over the model, not a change to it.
        /// </summary>
        private string _focusTable;
        private string _focusTitle;
        private int _focusHops;

        /// <summary>One-shot prefix for the status bar after a solution loads.</summary>
        private string _selectionNote;
        private readonly ErdOptions _options = new ErdOptions();
        private readonly Timer _rebuildDebounce;
        private bool _suspendEntityEvents;

        public ErdVisualizerControl()
        {
            _rebuildDebounce = new Timer { Interval = 300 };
            _rebuildDebounce.Tick += (s, e) => { _rebuildDebounce.Stop(); Rebuild(); };

            // Before the menus are built: they take their ticks from _options.
            OptionsStore.Load(_options);
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
            _compareDrop = BuildCompareDropDown();

            _fitButton = new ToolStripButton("Zoom to Fit")
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Enabled = false
            };
            _fitButton.Click += (s, e) => _panel.ZoomToFit();

            // Only appears while focused: a visible way out of a filtered view,
            // so nobody is left wondering where their other tables went.
            _clearFocusButton = new ToolStripButton("Show all tables")
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Visible = false,
                ToolTipText = "Leave focus mode and go back to the ticked tables"
            };
            _clearFocusButton.Click += (s, e) => ClearFocus();

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
                _exportDrop, _compareDrop, new ToolStripSeparator(),
                _fitButton, _clearFocusButton, _findBox, new ToolStripSeparator(),
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
                // Ticking tables is an explicit statement of scope, so it takes
                // over from focus rather than being silently ignored by it.
                ForgetFocus();
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
            _panel.TableMoved += OnTableMoved;
            _panel.TableRightClicked += ShowTableMenu;
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

                // The caption follows the remembered mode, not a hardcoded default.
                if (item.Checked) drop.Text = "Columns: " + text.Replace("&", "&&");

                item.Click += (s, e) =>
                {
                    _options.AttributeMode = mode;
                    OptionsStore.Save(_options);
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
                item.CheckedChanged += (s, e) =>
                {
                    apply(item.Checked);
                    OptionsStore.Save(_options);
                    Rebuild();
                };
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

            var wrap = Toggle("Wrap wide rows", _options.WrapWideRanks, v => _options.WrapWideRanks = v);
            wrap.ToolTipText = "Stop any row of tables growing wider than the rest of the diagram " +
                               "by moving some tables down a row.";
            drop.DropDownItems.Add(new ToolStripSeparator());

            Toggle("N:N relationships", _options.IncludeManyToMany, v => _options.IncludeManyToMany = v);
            Toggle("Self-referential loops", _options.IncludeSelfReferential, v => _options.IncludeSelfReferential = v);
            Toggle("Related external tables", _options.IncludeExternalEntities, v => _options.IncludeExternalEntities = v);
            Toggle("Relationship labels", _options.ShowEdgeLabels, v => _options.ShowEdgeLabels = v);
            drop.DropDownItems.Add(new ToolStripSeparator());
            Toggle("System columns && relationships", _options.IncludeSystemRelationships,
                v => _options.IncludeSystemRelationships = v);

            drop.DropDownItems.Add(new ToolStripSeparator());
            var reset = new ToolStripMenuItem("Reset manual layout")
            {
                ToolTipText = "Tables you drag keep their position across rebuilds and sessions. " +
                              "This puts them all back."
            };
            reset.Click += (s, e) => ResetManualLayout();
            drop.DropDownItems.Add(reset);
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

            // The agent-facing formats live in their own submenu: they answer a
            // different question from the picture formats above.
            var kb = new ToolStripMenuItem("Knowledge base for AI agents")
            {
                ToolTipText = "Retrieval-shaped documentation for grounding a Copilot Studio " +
                              "agent: relationships written out from both sides, full column " +
                              "list, no diagram embedded."
            };

            var perTable = new ToolStripMenuItem("One file per table (folder)…")
            {
                ToolTipText = "Highest confidence: citations name the table, and a retrieved " +
                              "passage can never straddle two tables."
            };
            perTable.Click += (s, e) => ExportKnowledgeBaseFolder();

            var single = new ToolStripMenuItem("Single Markdown file…")
            {
                ToolTipText = "One document covering every table. Simpler to upload; " +
                              "citations name only the file."
            };
            single.Click += (s, e) => Export("Markdown (*.md)|*.md|Text file (*.txt)|*.txt", ".md",
                MarkdownExporter.Save);

            kb.DropDownItems.Add(perTable);
            kb.DropDownItems.Add(single);
            drop.DropDownItems.Add(kb);
            return drop;
        }

        /// <summary>
        /// The comparison workflow in one place: freeze the model now, compare
        /// against it later — or compare two frozen models without connecting
        /// to anything at all.
        /// </summary>
        private ToolStripDropDownButton BuildCompareDropDown()
        {
            var drop = new ToolStripDropDownButton("Compare")
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ToolTipText = "Compare this solution's data model with another environment or an earlier point in time"
            };

            var save = new ToolStripMenuItem("Save model snapshot…")
            {
                ToolTipText = "Freeze the loaded solution's data model in a file, to compare against " +
                              "later or from another environment."
            };
            save.Click += (s, e) => SaveSnapshot();

            var withSnapshot = new ToolStripMenuItem("Compare with a snapshot…")
            {
                ToolTipText = "What changed between a saved snapshot and the solution loaded now?"
            };
            withSnapshot.Click += (s, e) => CompareWithSnapshot();

            var twoSnapshots = new ToolStripMenuItem("Compare two snapshots…")
            {
                ToolTipText = "Compare two saved snapshots. No connection needed."
            };
            twoSnapshots.Click += (s, e) => CompareTwoSnapshots();

            drop.DropDownItems.Add(save);
            drop.DropDownItems.Add(new ToolStripSeparator());
            drop.DropDownItems.Add(withSnapshot);
            drop.DropDownItems.Add(twoSnapshots);
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

                    // Each solution keeps its own arrangement; one solution's
                    // positions mean nothing in another.
                    _solutionKey = solution.UniqueName;
                    _pinned = LayoutStore.Load(_solutionKey);
                    ForgetFocus();

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
            // Empty the list BEFORE anything reads it back. Otherwise the
            // capture step inside FillEntityList harvests the PREVIOUS
            // solution's checkboxes — handing any table the two solutions share
            // its old tick, and writing the old solution's tables into this
            // one's saved selection.
            _suspendEntityEvents = true;
            _entityList.Items.Clear();
            _suspendEntityEvents = false;

            // Last session's ticks where the table was known then; tables new
            // to the solution get the usual default for its size.
            bool newTableDefault = SolutionTables().Count() <= LargeSolutionThreshold;
            var saved = SelectionStore.Load(_solutionKey);
            var resolved = SelectionStore.Resolve(
                SolutionTables().Select(e => e.LogicalName), saved, newTableDefault);

            _checkedByName.Clear();
            foreach (var tick in resolved) _checkedByName[tick.Key] = tick.Value;

            _entitySearch.Clear();
            FillEntityList();

            // Only worth saying when it changed something: a remembered
            // selection that equals the defaults is indistinguishable from none.
            _selectionNote = saved.Count > 0 && resolved.Any(t => t.Value != newTableDefault)
                ? "Restored your table selection · "
                : null;
        }

        /// <summary>
        /// Remembers the current solution's ticks. Only its own tables are
        /// written, so nothing from another solution can leak into the file.
        /// </summary>
        private void SaveSelection()
        {
            if (_model == null || string.IsNullOrEmpty(_solutionKey)) return;

            var ticks = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var table in SolutionTables())
            {
                bool ticked;
                ticks[table.LogicalName] = _checkedByName.TryGetValue(table.LogicalName, out ticked) && ticked;
            }
            SelectionStore.Save(_solutionKey, ticks);
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
            ForgetFocus();
            Rebuild();
        }

        // ---------------------------------------------------------- building

        private void Rebuild()
        {
            if (_model == null) return;

            CaptureChecklist();
            // Every checklist change funnels through here, so this is the one
            // place the selection needs saving.
            SaveSelection();

            // Focus overrides the ticked selection rather than editing it, so
            // clearing focus returns to exactly the scope the user had chosen.
            _options.SelectedEntities = _focusTable != null
                ? ErdGraphBuilder.Neighbourhood(_model, _options, _focusTable, _focusHops)
                : new HashSet<string>(
                    _checkedByName.Where(kv => kv.Value).Select(kv => kv.Key),
                    StringComparer.OrdinalIgnoreCase);

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
                // Hand-placed tables go back where they were left; the layout
                // engine has no memory of them across a rebuild.
                PinnedLayout.Apply(diagram, _pinned);

                _panel.SetDiagram(diagram);
                _panel.ZoomToFit();

                int tables = diagram.Graph.Nodes.Count;
                int rels = diagram.Graph.Edges.Count;
                int placed = diagram.Graph.Nodes.Count(n => n.Pinned);
                _status.ForeColor = Color.DimGray;

                if (tables == 0 && _focusTable == null)
                {
                    // Derived rather than set when the list is filled: anything
                    // set there was overwritten by this very line before anyone
                    // could read it.
                    _status.Text = "No tables ticked — tick tables in the list to draw them";
                }
                else
                {
                    _status.Text = (_selectionNote ?? "") +
                                   (_focusTable != null
                                       ? "Focused on " + _focusTitle +
                                         (_focusHops == 1 ? " (direct) · " : " (two hops) · ")
                                       : "") +
                                   tables + " tables · " + rels + " relationships" +
                                   (placed > 0 ? " · " + placed + " placed by hand" : "");
                }
                _selectionNote = null;   // said once, on the first draw after loading
                UpdateFocusButton();
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

        // ------------------------------------------------------------- focus

        /// <summary>
        /// The right-click menu on a table: the entry point to focus mode,
        /// which is how anyone explores a model too big to read at once.
        /// </summary>
        private void ShowTableMenu(ErdNode node, Point where)
        {
            if (node?.Entity == null) return;

            var name = node.Title ?? node.Id;
            var menu = new ContextMenuStrip();

            var direct = new ToolStripMenuItem($"Focus on {name} — direct relationships");
            direct.Click += (s, e) => FocusOn(node, 1);

            var twoHops = new ToolStripMenuItem($"Focus on {name} — two hops");
            twoHops.Click += (s, e) => FocusOn(node, 2);

            var showAll = new ToolStripMenuItem("Show all tables") { Enabled = _focusTable != null };
            showAll.Click += (s, e) => ClearFocus();

            menu.Items.Add(direct);
            menu.Items.Add(twoHops);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(showAll);
            menu.Closed += (s, e) => menu.Dispose();
            menu.Show(_panel, where);
        }

        private void FocusOn(ErdNode node, int hops)
        {
            _focusTable = node.Id;
            _focusTitle = node.Title ?? node.Id;
            _focusHops = hops;
            Rebuild();
        }

        private void ClearFocus()
        {
            if (_focusTable == null) return;
            ForgetFocus();
            Rebuild();
        }

        /// <summary>Drops focus without redrawing, for callers about to rebuild anyway.</summary>
        private void ForgetFocus()
        {
            _focusTable = null;
            _focusTitle = null;
            _focusHops = 0;
        }

        private void UpdateFocusButton()
        {
            _clearFocusButton.Visible = _focusTable != null;
            _clearFocusButton.Text = _focusTable != null
                ? "Show all tables (focused on " + _focusTitle + ")"
                : "Show all tables";
        }

        /// <summary>
        /// A drag finished: remember where every hand-placed table now sits, so
        /// the next rebuild and the next session can put them back.
        /// </summary>
        private void OnTableMoved()
        {
            var diagram = _panel.Diagram;
            if (diagram == null) return;

            foreach (var pin in PinnedLayout.Collect(diagram))
                _pinned[pin.Key] = pin.Value;

            LayoutStore.Save(_solutionKey, _pinned);

            int placed = _pinned.Count;
            _status.ForeColor = Color.DimGray;
            _status.Text = diagram.Graph.Nodes.Count + " tables · " +
                           diagram.Graph.Edges.Count + " relationships · " +
                           placed + " placed by hand";
        }

        /// <summary>Throws away the manual arrangement and lays out afresh.</summary>
        private void ResetManualLayout()
        {
            if (_pinned.Count == 0)
            {
                MessageBox.Show(this, "No tables have been placed by hand.", "Nothing to reset",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var message = $"Put all {_pinned.Count} hand-placed table(s) back where the layout " +
                          "engine wants them?\n\nThis cannot be undone.";
            if (MessageBox.Show(this, message, "Reset manual layout",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _pinned.Clear();
            LayoutStore.Delete(_solutionKey);
            Rebuild();
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

        /// <summary>
        /// Writes the knowledge base as one Markdown file per table, plus an
        /// overview file, into a folder the user picks.
        /// </summary>
        private void ExportKnowledgeBaseFolder()
        {
            var diagram = _panel.Diagram;
            if (diagram == null || diagram.Graph.Nodes.Count == 0)
            {
                MessageBox.Show(this, "Generate a diagram first.", "Nothing to export",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int tables = diagram.Graph.Nodes.Count(n => n.Entity != null && !n.Entity.IsExternal);

            string folder;
            using (var dlg = new FolderBrowserDialog
            {
                Description = $"Choose where to put the knowledge base ({tables} table files plus " +
                              "an overview). A subfolder is created for this solution."
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                folder = dlg.SelectedPath;
            }

            try
            {
                Cursor = Cursors.WaitCursor;
                var result = MarkdownExporter.SavePerTable(diagram, folder);

                var message = $"Wrote {result.FileCount} files to:\n{result.FolderPath}\n\n";

                // Leftovers from an earlier export describe tables that may no
                // longer exist; uploaded together they would ground the agent
                // in a model that is out of date.
                if (result.StaleFiles.Count > 0)
                {
                    message += $"⚠ {result.StaleFiles.Count} other Markdown file(s) were already in " +
                               "that folder and were NOT written by this export — likely left over " +
                               "from an earlier run. Delete them before uploading, or the agent " +
                               "will also learn tables that are no longer in the solution.\n\n";
                }

                message += "Upload this folder as a Copilot Studio knowledge source, or sync it to " +
                           "a SharePoint library and point the agent there.\n\nOpen the folder now?";

                var icon = result.StaleFiles.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information;
                if (MessageBox.Show(this, message, "Knowledge base exported",
                        MessageBoxButtons.YesNo, icon) == DialogResult.Yes)
                {
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + result.FolderPath + "\"");
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

        // ---------------------------------------------------------- comparing

        /// <summary>
        /// The connection's name, used to label which side is which.
        ///
        /// Read by reflection on purpose. <c>ConnectionDetail</c> lives in the
        /// host's McTools.Xrm.Connection assembly, and binding to it at compile
        /// time fails outright (the package resolves an older version than
        /// XrmToolBox.Extensibility was built against) — and would otherwise tie
        /// this plugin to one host version. A label is a nicety: if the host
        /// renames anything, the snapshot is simply unlabelled.
        /// </summary>
        private string EnvironmentName()
        {
            try
            {
                const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance;
                var detail = GetType().GetProperty("ConnectionDetail", Public)?.GetValue(this);
                if (detail == null) return null;

                string Read(string property)
                    => detail.GetType().GetProperty(property, Public)?.GetValue(detail) as string;

                var name = Read("ConnectionName");
                return string.IsNullOrWhiteSpace(name) ? Read("OrganizationFriendlyName") : name;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>The loaded model as a snapshot taken right now.</summary>
        private ModelSnapshot CurrentSnapshot() => new ModelSnapshot
        {
            CapturedOn = DateTime.UtcNow,
            Environment = EnvironmentName(),
            ToolVersion = typeof(ErdVisualizerControl).Assembly.GetName().Version.ToString(),
            Model = _model
        };

        private bool RequireModel()
        {
            if (_model != null) return true;
            MessageBox.Show(this, "Load a solution first.", "No solution loaded",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        private void SaveSnapshot()
        {
            if (!RequireModel()) return;

            var snapshot = CurrentSnapshot();
            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = SnapshotStore.DialogFilter;
                dialog.FileName = MakeSafeFileName(
                    (_model.Solution?.UniqueName ?? "solution") +
                    (string.IsNullOrEmpty(snapshot.Environment) ? "" : "-" + snapshot.Environment) +
                    "-" + DateTime.Now.ToString("yyyyMMdd")) + SnapshotStore.Extension;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    SnapshotStore.Save(snapshot, dialog.FileName);
                    MessageBox.Show(this,
                        $"Saved the data model of {_model.Solution?.FriendlyName} " +
                        $"({_model.Entities.Count(e => !e.IsExternal && !e.IsIntersect)} tables).\n\n" +
                        "Use Compare → Compare with a snapshot… later, or from another environment, " +
                        "to see what changed.",
                        "Snapshot saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not save the snapshot:\n\n" + ex.Message, "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void CompareWithSnapshot()
        {
            if (!RequireModel()) return;

            var baseline = PickSnapshot("Choose the snapshot to compare the loaded solution against");
            if (baseline == null) return;

            ShowComparison(baseline, CurrentSnapshot());
        }

        private void CompareTwoSnapshots()
        {
            var baseline = PickSnapshot("Choose the BASELINE snapshot — the earlier one, or the reference environment");
            if (baseline == null) return;

            var current = PickSnapshot("Choose the snapshot to compare with it");
            if (current == null) return;

            ShowComparison(baseline, current);
        }

        private ModelSnapshot PickSnapshot(string title)
        {
            using (var dialog = new OpenFileDialog { Filter = SnapshotStore.DialogFilter, Title = title })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return null;
                try
                {
                    return SnapshotStore.Load(dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Cannot read snapshot",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
            }
        }

        /// <summary>Summarises the comparison, then offers the full change report.</summary>
        private void ShowComparison(ModelSnapshot baseline, ModelSnapshot current)
        {
            ErdDiff diff;
            try
            {
                Cursor = Cursors.WaitCursor;
                diff = ModelDiff.Compare(baseline.Model, current.Model);
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            var heading = "Baseline: " + baseline.Describe() + "\nCurrent: " + current.Describe() + "\n\n";

            if (!diff.HasChanges)
            {
                if (MessageBox.Show(this, heading + "No differences: the two data models match.\n\n" +
                                          "Save a report saying so anyway?",
                        "No differences", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
                    return;
            }
            else
            {
                int systemCount = diff.Relationships.Count(r => r.IsSystem);
                var summary = heading +
                              Tally("Tables", diff.Tables.Select(t => t.Kind)) + "\n" +
                              Tally("Relationships", diff.Relationships.Where(r => !r.IsSystem).Select(r => r.Kind)) +
                              (systemCount > 0 ? $"\n(plus {systemCount} system relationship change(s))" : "") +
                              "\n\nSave the full change report?";
                if (MessageBox.Show(this, summary, "Data model changes",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
                    return;
            }

            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = "Markdown (*.md)|*.md|Text file (*.txt)|*.txt";
                dialog.FileName = MakeSafeFileName(
                    (current.Model?.Solution?.UniqueName ?? "model") + "-changes-" +
                    DateTime.Now.ToString("yyyyMMdd")) + ".md";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    DiffReport.Save(baseline, current, diff, dialog.FileName);
                    System.Diagnostics.Process.Start(dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not save the report:\n\n" + ex.Message, "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static string Tally(string what, IEnumerable<ChangeKind> kinds)
        {
            var list = kinds.ToList();
            if (list.Count == 0) return what + ": no changes";
            return what + ": " + string.Join(", ",
                new[] { ChangeKind.Added, ChangeKind.Removed, ChangeKind.Changed }
                    .Where(k => list.Contains(k))
                    .Select(k => list.Count(x => x == k) + " " + k.ToString().ToLowerInvariant()));
        }

        private static string MakeSafeFileName(string name)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Length > 80 ? name.Substring(0, 80) : name;
        }
    }
}
