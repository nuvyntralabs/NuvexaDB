# NuvexaDB

Embedded NoSQL database for **.NET** and **.NET MAUI**, with a Native AOT C ABI for **Java**, **Kotlin**, **Swift**, **Flutter**, **React Native**, **Python**, **Node.js**, **Go**, and **C++**. One portable binary **`.nvx`** file (BSON documents on data pages, AES-256-GCM encryption), and a desktop explorer for Windows, macOS, and Linux.

**Package:** `Nuventra.NuvexaDB`  
**Version:** 1.0.3  
**Author:** Niladri Prasad Padhy / Nuventra  
**License:** MIT  
**Product name:** NuvexaDB (this repo). The MauiEssentials catalog is published under **Nuvyntra** Labs — the spellings are intentional.

## Why NuvexaDB

NuvexaDB is a standalone **embedded NoSQL** engine: collections, **NQL** (Nuvexa Query Language), optional AES-256-GCM, one `.nvx` application file, and an IDE. It runs in-process. It is not a network server.

## Install

```bash
dotnet add package Nuventra.NuvexaDB
```

Do not publish this package from a local clone. CI on `main` and PRs **runs all C# tests first**, then builds and uploads GitHub artifacts (nupkg + snupkg, native ABI, Explorer installers, VS Code / Visual Studio VSIX, and language SDK packs). Coverage / test-result zips are not uploaded. Those everyday runs **do not** create or update [Releases](https://github.com/nuvyntralabs/NuvexaDB/releases). nuget.org, GitHub Packages, and the version / NuGet validation jobs are commented out until multi-host publishing (.NET, Maven, npm, …) is decided. Uncomment those steps in `NuvexaDB/.github/workflows/ci.yml` to restore them.

After each CI run, downloads are on that run’s **Artifacts** list (temporary; they expire):

| Artifact | Contents |
| --- | --- |
| `NuvexaDB-NuGet` | Engine `nupkg` + `snupkg` (not pushed) |
| `NuvexaDB-Native-<rid>` | C ABI (`libnuvexa` / `nuvexa.dll`) |
| `NuvexaDB-Native-android-arm64` | Bionic `libnuvexa.so` for the Android AAR |
| `NuvexaDB-Native-iOS` | `Nuvexa.xcframework` (`ios-arm64` + `iossimulator-arm64`) + `nuvexa.h` |
| `NuvexaDB-Data-Studio-<rid>` | Data Studio installer (msi / pkg / deb / rpm) |
| `NuvexaDB-VS-Code` | VS Code / Cursor VSIX |
| `NuvexaDB-Visual-Studio` | Visual Studio VSIX |
| `NuvexaDB-Java-<rid>` | Java / Kotlin library |
| `NuvexaDB-Python-<rid>` | Python library |
| `NuvexaDB-Node-<rid>` | Node.js library |
| `NuvexaDB-Go-<rid>` | Go library |
| `NuvexaDB-Cpp-<rid>` | C++ library |
| `NuvexaDB-Flutter-<rid>` | Flutter / Dart library |
| `NuvexaDB-Swift-osx-arm64` | Swift library |
| `NuvexaDB-Android` | Android AAR |
| `NuvexaDB-React-Native-<rid>` | React Native JS pack |
| `NuvexaDB-React-Native-Android` | React Native Android AAR + `libnuvexa.so` |
| `NuvexaDB-React-Native-iOS` | Compiled `NuvexaDB.mm` + host ABI tests |

Explorer installers (single-file app inside a native package):

- **Windows** — `NuvexaDB-Explorer-*-win-x64.msi` and `NuvexaDB-Explorer-*-win-arm64.msi`
- **macOS** — `NuvexaDB-Explorer-*-osx-*.pkg` (unsigned for now; always installs to `/Applications`)
- **Linux** — `nuvexadb-explorer_*_amd64.deb` / `nuvexadb-explorer-*-x86_64.rpm` (x64) and `nuvexadb-explorer_*_arm64.deb` / `nuvexadb-explorer-*-aarch64.rpm` (ARM64)

### Public downloads (you control this)

https://github.com/nuvyntralabs/NuvexaDB/releases stays empty (or on the last version you published) until you **choose** to release.

A push to `main` never updates that page. To publish a version, bump `<Version>` and `<PackageVersion>` in `Directory.Build.props` (keep them equal), then run:

```bash
python3 .github/scripts/check-versions.py --repo-root . --write
```

That copies the same version onto NuGet, Java, Android (`versionName` + `versionCode` = major×10000+minor×100+patch), Python, Node, React Native, Flutter, Go, C++, Swift, Data Studio installers, VS Code, and Visual Studio. CI fails if any of those drift. Then push `main` and tag:

```bash
git tag v1.0.3
git push origin v1.0.3
```

That tagged CI run copies the same zips onto the release **only after every CI job is green**. If any job fails, the run is red and [Releases](https://github.com/nuvyntralabs/NuvexaDB/releases) is not created or updated. Fix the failure and push the tag again (or a new tag) when the run succeeds. If `v<Version>` is **already published**, CI stops after version alignment — it does not rebuild. Bump `Directory.Build.props` to start a new build. The public URL is then `https://github.com/nuvyntralabs/NuvexaDB/releases/tag/v1.0.3`. Users download only the zip they need.

## Quick start

```csharp
using Nuventra.NuvexaDB;

await using var db = NuvexaDatabase.Create("app.nvx", new NuvexaCreateOptions
{
    EncryptionKey = "correct-horse"
});

var users = db.GetCollection("users");
await users.InsertAsync(NuvexaDocument.Parse("""{"name":"Ada","age":36}"""));
await users.EnsureIndexAsync("age");
await users.EnsureIndexAsync(["city", "status"]);

var rows = await db.ExecuteAsync("""db.users.find({ age: { $gte: 21 } }).sort({ name: 1 }).limit(20)""");
```

Opening an encrypted file **without** a key throws `NuvexaEncryptionException` (fail-closed). A tampered or corrupt `.nvx` throws `NuvexaIntegrityException` and is not opened. The Explorer, Visual Studio editor, and VS Code / Cursor editor prompt for the key.

One process may open a path at a time. Concurrent `Find` / reads on an open handle are allowed; writes stay exclusive. `CompactAsync` keeps encryption and leaves the handle open. `BackupAsync` / `RestoreAsync` copy the `.nvx`. Format version `1` is frozen (page size, WAL, index keys). Numeric range IXSCAN is not order-preserving; index equality, string ranges, and compound equality. `$lookup` refuses a foreign collection larger than `LookupMaxDocuments` (default 100 000; `0` disables). Limits: `NuvexaLimits`.

```csharp
if (NuvexaDatabase.IsEncrypted(path))
{
    // IDE: ask the user. Library: throw if EncryptionKey is missing.
}

await using var opened = NuvexaDatabase.Open(path, new NuvexaOpenOptions { EncryptionKey = key });
```

## Query language

```javascript
db.users.find({ "address.city": "Bengaluru", age: { $gte: 21 } })
  .sort({ lastName: 1 })
  .skip(0)
  .limit(20)
  .project({ email: 1 })

// Page 2 of 200 rows (same as .skip(200).limit(200)):
db.tickets.find({}).page(2, 200)
```

Operators: `$eq $ne $gt $gte $lt $lte $in $nin $and $or $exists $regex`. Updates: `$set $unset $inc $push $pull`.

Aggregation: `$match $project $sort $skip $limit $count $group $lookup`. Typed LINQ: `GetCollection<T>().Where(p => p.Age >= 21)`. Files: `db.Files.UploadAsync` / `DownloadAsync` (`fs.files` / `fs.chunks`). `Find().ToAsyncEnumerable()` streams pages. `await using var tx` rolls back if you do not `CommitAsync`.

## Product layers

| Layer | Project | Role |
| --- | --- | --- |
| **Database core** | `src/Nuventra.NuvexaDB/Engine`, `Encryption`, `Query` | Pages, WAL, B+tree, AES-256-GCM, filters, aggregation |
| **Access library** | `Nuventra.NuvexaDB` | `NuvexaDatabase` / `NuvexaCollection` / `AddNuvexaDB` for apps |
| **Nuvexa Data Studio** | `Nuventra.NuvexaDB.Explorer` | Avalonia desktop IDE + [Plugin.Avalonia.MVVMExpress](https://www.nuget.org/packages/Plugin.Avalonia.MVVMExpress) on Windows, macOS, and Linux |
| **Editor extensions** | `Nuventra.NuvexaDB.VSCode`, `Nuventra.NuvexaDB.VisualStudio` | Custom editor / tool window over the same `ExplorerSession` (browse filter, 200-row pager, query examples, explain) |
| **C ABI** | `Nuventra.NuvexaDB.Native` | Native AOT shared library (`nuvexa_create` / `execute` / …). Same engine; JSON in, JSON out. |
| **Language SDKs** | `bindings/jvm`, `android`, `swift`, `flutter`, `react-native`, `python`, `node`, `go`, `cpp` | Thin overlays over `nuvexa.h` (ABI v2) |

Supporting: `Nuventra.NuvexaDB.Tools` (session + grid cache), `nuvexa` CLI (`browse` / `samples` / `explain` / `backup` / `restore`; used by VS Code).

## Language bindings

Do not rewrite the engine. Publish the C ABI, then call it:

```bash
src/Nuventra.NuvexaDB.Native/publish.sh
```

```kotlin
NuvexaDatabase.create("app.nvx", "correct-horse").use { db ->
    db.insert("users", """{"name":"Ada","age":36}""")
    db.execute("db.users.find({ age: { \$gte: 21 } }).limit(20)")
}
```

```swift
let db = try NuvexaDatabase.create("app.nvx", key: "correct-horse")
_ = try db.insert(collection: "users", json: #"{"name":"Ada","age":36}"#)
_ = try db.execute("db.users.find({ age: { $gte: 21 } }).limit(20)")
```

```dart
final db = NuvexaDatabase.create('app.nvx', key: 'correct-horse');
db.insert('users', '{"name":"Ada","age":36}');
db.execute('db.users.find({ age: { \$gte: 21 } }).limit(20)');
```

```js
const db = await NuvexaDatabase.create("app.nvx", "correct-horse");
await db.insert("users", JSON.stringify({ name: "Ada", age: 36 }));
await db.execute("db.users.find({ age: { $gte: 21 } }).limit(20)");
```

Desktop CI RIDs: `osx-*`, `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`. Android JNI uses Native AOT `linux-bionic-arm64` (not `android-arm64`). iOS CI publishes `ios-arm64` + `iossimulator-arm64` with `PublishAotUsingRuntimePack` and packs `Nuvexa.xcframework`. ABI v2 adds catalog, transactions, and path-based GridFS. Details: [docs/bindings.md](docs/bindings.md). Language samples live inside each binding project.

## Samples

Each .NET sample is its own solution (`*.sln` next to the project). `NuvexaDB.sln` is the engine, tests, tools, and IDE hosts only.

- `samples/Console/Console.sln` — create, encrypt, fail-closed open, query
- `samples/Maui/MauiSample.sln` — Android / iOS / Mac Catalyst / Windows app-data `.nvx`
- `samples/Avalonia/AvaloniaSample.sln` — desktop file + query
- `samples/Wpf/WpfSample.sln` — WPF (`net10.0-windows10.0.17763.0`)
- `samples/WinUI/WinUISample.sln` — unpackaged WinUI 3 (`net10.0-windows10.0.19041.0`)
- `samples/Uno/UnoSample.sln` — Uno Platform desktop (`net10.0-desktop`; not WASM)
- `bindings/*/examples` (and JVM `src/sampleJava` / `src/sampleKotlin`) — same flow over the C ABI, kept next to each SDK

## Benchmarks

```bash
dotnet run --project benches/Nuventra.NuvexaDB.Benchmarks -c Release -- --gate
```

`--gate` also freezes 10k insert (≤ 1.5× LiteDB), encrypted point-get (≤ +30%), and 100k-set point-get (≤ 3× SQLite). `--crore` is a local 10 million document write / index / query bench (not CI; see [docs/benchmarks.md](docs/benchmarks.md)). Explorer installers and `.nvx` file-association scripts live in `src/Nuventra.NuvexaDB.Explorer/packaging/`.

## Docs

- [Platform integration](docs/Integration/README.md) — download the exact Release zip, empty project, create/open/close, collections, documents, encryption password
- [White paper](docs/whitepaper.md) — engine architecture, security, performance, platform libraries, IDEs, roadmap
- [Explorer IDE](docs/explorer.md) — Data Studio / VS / VS Code capability inventory
- [Change log](docs/changelog.md) — unreleased engine, Explorer, and bench notes
- [File format](docs/format.md)
- [Engine sharing architecture](docs/architecture.md) — one engine, one C ABI, thin SDKs
- [Language bindings](docs/bindings.md)
- [NQL](docs/query.md)
- [Benchmarks](docs/benchmarks.md)
