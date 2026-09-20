# nuvexa

A [dotnet tool](https://learn.microsoft.com/dotnet/core/tools/global-tools) for `.nvx` files: browse, NQL, backup, restore, and encryption-key change.

```bash
dotnet tool install -g Nuventra.NuvexaDB.Cli --source https://api.nuget.org/v3/index.json
nuvexa --help
nuvexa info app.nvx
nuvexa query app.nvx 'db.users.find({ age: { $gte: 21 } }).limit(20)'
```

Do not `dotnet add package Nuventra.NuvexaDB.Cli` into an app. The engine library is [`Nuventra.NuvexaDB`](https://www.nuget.org/packages/Nuventra.NuvexaDB).

The same `nuvexa` binary is also bundled in the VS Code / Cursor and Visual Studio VSIX (`cli/nuvexa`). Those editors prefer the bundled copy, then fall back to this tool on `PATH`.

Requires the .NET 10 SDK. Publishing is pipeline-only — do not `dotnet nuget push` from a local clone.

Repo: https://github.com/nuvyntralabs/NuvexaDB
