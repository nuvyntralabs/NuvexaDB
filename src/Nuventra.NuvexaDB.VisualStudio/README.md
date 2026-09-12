# NuvexaDB for Visual Studio

`NuvexaToolWindow` opens a `.nvx` file through `ExplorerSession`, loads the collection/index tree, materializes a document grid, imports/exports JSON, compacts, and prompts up to three times for an encryption key.

On Windows the `Vsix/` sources compile with the Visual Studio SDK (`VSSDK`): `NuvexaPackage`, `.nvx` editor factory, WPF tool-window pane, and key dialog. Pack `source.extension.vsixmanifest` + `NuvexaDB.pkgdef` with the Visual Studio SDK to register the editor.

On macOS / Linux the same project builds the host API only (`NuvexaToolWindow`, `NuvexaGuids`).
