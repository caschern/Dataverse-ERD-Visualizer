# Dataverse ERD Visualizer

An [XrmToolBox](https://www.xrmtoolbox.com/) tool that generates interactive **Entity
Relationship Diagrams** from any solution in a Power Platform environment.

Pick a solution → get a crow's-foot ERD of its tables: columns with PK / name / FK
badges, 1:N and N:N relationships, self-referential loops, and related out-of-solution
tables as grey stubs. Drag tables to fine-tune the layout, then export.

Sibling project of [Dataverse Process Mapper](https://github.com/caschern/Dataverse-Process-Mapper)
and built on the same custom diagram engine (GDI+/SVG/PDF surfaces + Sugiyama layout).

## Features

- **Solution picker** — lists every visible solution (name, version, managed, publisher).
- **Table checklist** — prune the diagram without refetching; filters for big solutions.
- **Noise control** — system columns/relationships (`createdby`, `owner*`, currency…)
  are hidden by default; toggle them back when you really want them.
- **Column display modes** — Keys & lookups (default) · Custom only · All (capped) · None.
- **Crow's-foot notation** — tick = one side, foot = many side, dashed = N:N
  (labeled with the intersect table), bottom-corner loops for self-referential lookups.
- **Segmented solution aware** — honours `rootcomponentbehavior` and attribute
  components, so "do not include subcomponents" tables only show their real columns.
- **Interactive preview** — zoom (Ctrl+wheel), pan, click a table for a full details
  pane (all columns + relationships), drag boxes to reposition, find-by-name.
- **Exports** — PNG, SVG (Visio/draw.io/Figma-editable), vector PDF, a self-contained
  HTML data dictionary, a Mermaid `erDiagram` for wikis and markdown, and a
  **Markdown knowledge base for AI agents**.

### Knowledge base export (Copilot Studio and similar)

`Export → Knowledge base for AI agents` writes documentation shaped for retrieval rather
than for reading, in two forms:

| | **One file per table** (recommended) | **Single Markdown file** |
|---|---|---|
| Citations | name the table | name only the document |
| Chunk bleed | impossible — file boundaries are hard | a passage can straddle two tables |
| Upload | a folder of N files | one file |

Both share the same content rules:

- each table documented on its own, naming itself in full rather than saying "it",
  because a chunk is retrieved without the sections around it;
- relationships written as sentences from **both** sides — a lookup listed only on the
  child would never surface when asking what references the parent;
- columns as self-describing bullets, not a table: a chunk boundary inside a Markdown
  table strands rows from their header and the model has to guess what each cell meant;
- the full column list regardless of the diagram's column display mode;
- an overview naming the model's hub tables, for orientation questions;
- no diagram embedded — image geometry would swamp every chunk.

Save as `.md`, or as `.txt` if your agent platform does not accept Markdown; the content
is identical and the headings still chunk correctly. The per-table folder can also be
synced to a SharePoint library and indexed from there, which gives you versioning and
access control.

## Install (local)

1. Build: `dotnet build DataverseErdVisualizer.csproj -c Release`
2. Copy `bin\Release\DataverseErdVisualizer.dll`, `PdfSharp.dll` and
   `PdfSharp.Charting.dll` to `%AppData%\MscrmTools\XrmToolBox\Plugins`
3. Delete `%AppData%\MscrmTools\XrmToolBox\Plugins\manifest.json`
   (XrmToolBox caches plugin tiles by assembly version; it rebuilds on launch)
4. Start XrmToolBox → **Dataverse ERD Visualizer**

## Usage

1. **Load Solutions**, select one — metadata is fetched in a single
   `RetrieveMetadataChanges` call scoped to the solution's tables.
2. Tick/untick tables, switch column modes, toggle N:N / self-loops / external
   stubs / labels from the toolbar.
3. Click a table for its full column and relationship list.
4. **Export** from the toolbar dropdown.

Solutions with more than 100 tables start with nothing checked — tick the areas you
want; everything is already fetched, so re-rendering is instant.

## Project layout

| Area | Purpose |
|---|---|
| `Data/` | `solution` / `solutioncomponent` queries + `RetrieveMetadataChanges` mapping |
| `Models/` | SDK-free solution model (`ErdModel`) and diagram model (`ErdGraph`) |
| `ErdGraphBuilder.cs` | model + display options → graph (filters, stubs, parallel-edge fanning) |
| `Layout/` | box sizing and the adapted Sugiyama layout (ranks, lanes, rails, loops) |
| `Rendering/` | `IDiagramSurface` + GDI/SVG/PDF backends, `ErdRenderer` (boxes, crow's feet) |
| `UI/` | zoomable/drag-able diagram panel, entity details pane |
| `Exporters/` | PNG · SVG · PDF · HTML data dictionary · Mermaid |
| `Tests/` | xunit suite — builder rules, layout invariants, end-to-end render smoke |

## Build notes

- Targets **net48** (XrmToolBox requirement); builds fine with the .NET 10 SDK.
- `XrmToolBoxPackage` / CRM SDK are compile-time only (`ExcludeAssets=runtime`) —
  the host provides them. Only the plugin DLL + PdfSharp ship.
- Tests: `dotnet test Tests` (no Dataverse connection needed).

## License

MIT
