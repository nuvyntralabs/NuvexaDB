# NuvexaDB — AI Coding Agent Guide

Embedded Mongo-like document database for .NET and .NET MAUI, plus a Native AOT C ABI (v2) for Java, Kotlin, Swift, Flutter, React Native, Python, Node, Go, and C++. Package `Nuventra.NuvexaDB`. File extension `.nvx`.

## When to consider

Local document cache, encrypted application files, **NQL** (Nuvexa Query Language) on device or desktop. Not a network server. Not a replacement for JobQueue or OfflineSync.

## Constraints

- Pure managed engine. Do not add native SQLite/RocksDB to `Nuventra.NuvexaDB`.
- Do not port pages, WAL, or encryption into Java, Kotlin, Swift, Dart, JavaScript, Python, Go, or C++. Bindings call `Nuventra.NuvexaDB.Native` (`nuvexa.h`).
- SQLite is allowed only in Tools (disposable explorer cache) and Benchmarks.
- Encrypted open without a key must throw `NuvexaEncryptionException` (C ABI `NUVEXA_ENCRYPTION`).
- Never `dotnet nuget push` from a local clone. Do not publish Maven, npm, pub.dev, or the Swift package from this workspace. Everyday CI uploads GitHub Actions artifacts only. A GitHub Release is created only on a `v*` tag. nuget.org / GitHub Packages push is commented out in `.github/workflows/ci.yml`.
- One product version: `Directory.Build.props` `<Version>` / `<PackageVersion>`. After a bump run `python3 .github/scripts/check-versions.py --repo-root . --write`. That covers .NET (inherit), Data Studio manifest, VS Code, Visual Studio, and every binding. Do not leave any of those on a different number.
- Before every `git commit` in this repo, ask whether this commit should make a GitHub Release. Default is no. Tag `v*` only after an explicit Yes. Prefix the commit with `NUVEXA_RELEASE=no` or `NUVEXA_RELEASE=yes`.
- v1 query is fluent + JSON. LINQ is an expression visitor (`ExpressionFilter`), not `IQueryable`. Do not add a provider that breaks Native AOT.
- Language SDKs expose NQL + CRUD JSON, plus ABI v2 catalog / tx / GridFS-by-path. Skip LINQ / `GetCollection<T>`.

## Layout

```
src/Nuventra.NuvexaDB            engine
src/Nuventra.NuvexaDB.Native     Native AOT C ABI (`nuvexa_*`)
src/Nuventra.NuvexaDB.Tools      explorer services
src/Nuventra.NuvexaDB.Explorer   Avalonia IDE (Plugin.Avalonia.MVVMExpress)
src/Nuventra.NuvexaDB.Cli        nuvexa tool
src/Nuventra.NuvexaDB.VisualStudio
src/Nuventra.NuvexaDB.VSCode
bindings/jvm  bindings/android  bindings/swift  bindings/flutter  bindings/react-native
bindings/python  bindings/node  bindings/go  bindings/cpp
tests/  tests/interop/  benches/  samples/  docs/
src/Nuventra.NuvexaDB.Explorer/packaging   .nvx file-association scripts
```

When you add or change Explorer IDE behavior, update [docs/explorer.md](docs/explorer.md). That page is the white-paper capability list. Record unreleased engine, Explorer, bench, and binding work in [docs/changelog.md](docs/changelog.md). How the engine is shared across languages: [docs/architecture.md](docs/architecture.md). Language ABI / SDK how-to: [docs/bindings.md](docs/bindings.md). Golden NQL cases live in [tests/interop/cases.json](tests/interop/cases.json).
