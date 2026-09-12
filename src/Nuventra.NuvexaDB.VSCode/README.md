# NuvexaDB for VS Code / Cursor

Custom editor for `*.nvx` files. Encrypted databases prompt for a key. The webview lists collections, runs Mongo-style queries, and shows index metadata.

Requires the CLI:

```bash
dotnet tool install -g Nuventra.NuvexaDB.Cli
```

Commands:

- Open a `.nvx` file (custom editor)
- `NuvexaDB: Find in collection` (`nuvexa.find`)
- `NuvexaDB: Change encryption key` (`nuvexa.changeKey`)

Build the webview host:

```bash
cd src/Nuventra.NuvexaDB.VSCode
npm install && npm run compile
```
