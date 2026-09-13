# NuvexaDB for VS Code / Cursor

Browse-only workbench for `*.nvx` files. Same `nuvexa` CLI as Explorer for tree / browse / query. The extension does not create, edit, or delete data.

- Activity Bar icon (**NuvexaDB**) opens **Database Browser**
- **Open Database** / **Close Database**
- Collapsible collections with **Columns** (and Indexes)
- **Browse Data**: filter (`status: paid` or NQL JSON), **Build filter**, find-in-page, JSON/Tree, 200-row Previous / Next, read-only grid
- **Execute Query**: NQL `find` / `aggregate`, examples, explain, read-only grid

Requires the CLI:

```bash
dotnet tool install -g Nuventra.NuvexaDB.Cli
```

Commands:

- Click the **NuvexaDB** cylinder icon in the left Activity Bar
- `NuvexaDB: Open Database` / `NuvexaDB: Close Database`
- Open a `.nvx` file (custom editor)

Build:

```bash
cd src/Nuventra.NuvexaDB.VSCode
npm install && npm run compile
```
