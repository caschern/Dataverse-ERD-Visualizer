# Publishing checklist (NuGet + XrmToolBox)

Battle-tested on Dataverse Process Mapper — the XrmToolBox portal validator is
strict and its error messages are misleading. Follow this exactly.

## 1. Version bump (lockstep — portal rejects mismatches)

- `DataverseErdVisualizer.csproj` → `<Version>`
- `DataverseErdVisualizer.nuspec` → `<version>` (same value)
- `<releaseNotes>` must be non-empty; `<summary>` must exist.

## 2. Build and pack

```
dotnet build DataverseErdVisualizer.csproj -c Release
nuget pack DataverseErdVisualizer.nuspec -OutputDirectory dist
```

- Use a **current nuget.exe** (6.x+). The PowerApps CLI copy is too old for `<icon>`.
- **Never `dotnet pack`** — it regenerates the dependency list from the csproj
  and replaces the required literal `XrmToolBox` dependency id with
  `XrmToolBoxPackage`, which the portal rejects.
- The NU5128 (framework group) and missing-readme warnings are expected; the
  proven-good package shape triggers both. Do not "fix" them.

## 3. Verify the package before pushing (immutable once pushed!)

`unzip -l dist\*.nupkg` must show exactly:

```
lib/net48/Plugins/DataverseErdVisualizer.dll
lib/net48/Plugins/DataverseErdVisualizer/PdfSharp.dll
lib/net48/Plugins/DataverseErdVisualizer/PdfSharp.Charting.dll
images/icon.png
```

Embedded nuspec must have:
- `<dependency id="XrmToolBox" version="1.2025.10.74" />` — literal id
  `XrmToolBox`, NOT `XrmToolBoxPackage`; version = a real XrmToolBox app release
  matching the compile-time XrmToolBoxPackage version.
- NO `<license>` element (the portal validator chokes on SPDX expressions).
- `<iconUrl>` pointing at the repo raw icon — verify it returns 200:
  `https://raw.githubusercontent.com/caschern/Dataverse-ERD-Visualizer/main/images/icon.png`
  (repo must be public and the file pushed).

## 4. Push to nuget.org

Either NuGet Package Explorer (Publish URL `https://www.nuget.org`, "Append
api/v2/package" checked) or:

```
nuget push dist\CasasHern.DataverseErdVisualizer.<version>.nupkg -Source https://api.nuget.org/v3/index.json -ApiKey <YOUR-API-KEY>
```

API key: nuget.org → Account → API Keys, scope **Push**, glob `CasasHern.*`.
(Not the "Trusted Publishing" form — that is GitHub Actions OIDC.)

## 5. Keep the listed-version set clean

The XrmToolBox portal validates **ALL listed versions** of the package. If a
bad version ever ships, unlist it on nuget.org (Manage Packages → uncheck
"List in search results") so only known-good versions stay listed, wait
~5–15 min for the search re-index, then re-submit.

## 6. XrmToolBox registration

- The in-app **Tool Library** discovers the package automatically via the
  `XrmToolBox` tag — no registration needed.
- The **xrmtoolbox.com portal** (Register a tool) validates the package with
  the rules above. Wait ~15 min after pushing for nuget.org indexing before
  submitting. If a registration ever gets stuck on an old bad version, delete
  the registration and create a fresh one rather than bumping endlessly.
