# NuvexaDB for Visual Studio

`NuvexaToolWindow` opens a `.nvx` file through `ExplorerSession`. It uses the same browse, query, and sample APIs as the Avalonia Explorer:

- Structure tree with Columns / Indexes and live row-count captions (`customers  (6)`)
- **Browse Data** tab: filter (NQL JSON or `status: paid`), field/operator **Build filter**, find-in-page, JSON/Tree, 200-row Previous / Next, results grid
- **Execute Query** tab: NQL examples, explain (`IXSCAN` / `COLLSCAN`), read-only results grid
- Import / export JSON, compact, encryption-key prompt (up to three attempts)

On Windows the `Vsix/` sources compile with the Visual Studio SDK (`VSSDK`): `NuvexaPackage`, `.nvx` editor factory, WPF tool-window pane, and key dialog. Pack `source.extension.vsixmanifest` + `NuvexaDB.pkgdef` with the Visual Studio SDK to register the editor.

On macOS / Linux the same project builds the host API only (`NuvexaToolWindow`, `NuvexaGuids`).
