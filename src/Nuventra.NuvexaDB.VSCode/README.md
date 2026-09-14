# NuvexaDB for VS Code / Cursor

Workbench for `*.nvx` files. The VSIX for your OS includes `nuvexa` — there is no separate CLI install.

- Activity Bar icon (**NuvexaDB**) opens **Database Browser**
- **Open Database** / **Close Database**
- Collapsible collections with **Columns** (and Indexes)
- **Browse Data**: filter (`status: paid` or NQL JSON), **Build filter**, find-in-page, JSON/Tree, 200-row Previous / Next, editable grid
- **Execute Query**: NQL `find` / `aggregate` / `update` / `delete`, examples, explain

Install the VSIX that matches this machine (`NuvexaDB-VS-Code-osx-arm64`, `win-x64`, `linux-x64`, …). CI runs `pack-vscode.sh`, which publishes the CLI into `cli/` and packs the VSIX.

Commands:

- Click the **NuvexaDB** cylinder icon in the left Activity Bar
- `NuvexaDB: Open Database` / `NuvexaDB: Close Database` / `NuvexaDB: About`
- Open a `.nvx` file (custom editor)

Build (extension tests only; does not publish `nuvexa`):

```bash
cd src/Nuventra.NuvexaDB.VSCode
npm install && npm test
```

Pack for this machine (publishes `nuvexa` automatically):

```bash
# from the NuvexaDB repo root
.github/scripts/pack-vscode.sh 1.0.3 osx-arm64 /tmp/nuvexadb.vsix
```
