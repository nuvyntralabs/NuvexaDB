# NuvexaDB — AI Coding Agent Guide

Embedded Mongo-like document database for .NET and .NET MAUI. Package `Nuventra.NuvexaDB`. File extension `.nvx`.

## When to consider

Local document cache, encrypted application files, **NQL** (Nuvexa Query Language) on device or desktop. Not a network server. Not a replacement for JobQueue or OfflineSync.

## Constraints

- Pure managed engine. Do not add native SQLite/RocksDB to `Nuventra.NuvexaDB`.
- SQLite is allowed only in Tools (disposable explorer cache) and Benchmarks.
- Encrypted open without a key must throw `NuvexaEncryptionException`.
- Never `dotnet nuget push` from a local clone.
- v1 query is fluent + JSON. LINQ is an expression visitor (`ExpressionFilter`), not `IQueryable`. Do not add a provider that breaks Native AOT.

## Layout

```
src/Nuventra.NuvexaDB            engine
src/Nuventra.NuvexaDB.Tools      explorer services
src/Nuventra.NuvexaDB.Explorer   Avalonia IDE (Plugin.Avalonia.MVVMExpress)
src/Nuventra.NuvexaDB.Cli        nuvexa tool
src/Nuventra.NuvexaDB.VisualStudio
src/Nuventra.NuvexaDB.VSCode
tests/  benches/  samples/  docs/
src/Nuventra.NuvexaDB.Explorer/packaging   .nvx file-association scripts
```

When you add or change Explorer IDE behavior, update [docs/explorer.md](docs/explorer.md). That page is the white-paper capability list. Record unreleased engine, Explorer, and bench work in [docs/changelog.md](docs/changelog.md).
