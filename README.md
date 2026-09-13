# NuvexaDB

Embedded, Mongo-inspired NoSQL for **.NET** and **.NET MAUI**, with a Native AOT C ABI for **Java**, **Kotlin**, **Swift**, **Flutter**, **React Native**, **Python**, **Node.js**, **Go**, and **C++**. One portable binary **`.nvx`** file (BSON documents on data pages, AES-256-GCM encryption), and a desktop explorer for Windows, macOS, and Linux.

**Package:** `Nuventra.NuvexaDB`  
**Author:** Niladri Prasad Padhy / Nuventra  
**License:** MIT  
**Product name:** NuvexaDB (this repo). The MauiEssentials catalog is published under **Nuvyntra** Labs — the spellings are intentional.

## Why NuvexaDB

MauiEssentials already uses SQLite as a **local cache / outbox** ([JobQueue](https://www.nuget.org/packages/Plugin.Maui.JobQueue), [OfflineSync](https://www.nuget.org/packages/Plugin.Maui.OfflineSync)). Those stay the right tools for durable jobs and sync. NuvexaDB is the general-purpose **document** file: collections, **NQL** (Nuvexa Query Language), encryption, and an IDE.

Use **SQLite** when you need SQL joins or an existing sqlite-net model. Use **NuvexaDB** when you want documents, a single `.nvx` application file, and fail-closed encryption.

## Install

```bash
dotnet add package Nuventra.NuvexaDB
```

Do not publish this package from a local clone. CI on `main` / tags / PRs **builds, tests, and uploads GitHub artifacts** (nupkg + snupkg, native ABI, Explorer installers, VS Code / Visual Studio VSIX, and language SDK packs). nuget.org, GitHub Packages, and the version / NuGet validation jobs are commented out until multi-host publishing (.NET, Maven, npm, …) is decided. Uncomment those steps in `NuvexaDB/.github/workflows/ci.yml` to restore them.

After each CI run, the job summary lists a **Download** link for every artifact (same pattern as the IDE / extensions):

| Artifact | Contents |
| --- | --- |
| `nuget-NuvexaDB` | `nupkg` + `snupkg` (not pushed) |
| `native-<rid>` | Desktop C ABI (`libnuvexa` / `nuvexa.dll`) |
| `native-android-arm64` | Bionic `libnuvexa.so` (`linux-bionic-arm64`) for the Android AAR |
| `native-ios` | `nuvexa.h` + compiled `NuvexaDB.o` (no xcframework; SDK rejects `ios-arm64` AOT) |
| `bindings-react-native-android` | React Native Android AAR + `libnuvexa.so` |
| `bindings-react-native-ios` | Compiled `NuvexaDB.mm` + host ABI tests |
| `explorer-<rid>` | Data Studio installer (msi / pkg / deb / rpm) |
| `vscode-NuvexaDB` | VS Code / Cursor VSIX |
| `vsix-NuvexaDB` | Visual Studio VSIX |
| `bindings-<sdk>-<rid>` | Language SDK pack after that SDK’s unit tests |
| `coverage-<os>` | Coverlet cobertura for engine + Explorer + Visual Studio |

Explorer installers (single-file app inside a native package):

- **Windows** — `NuvexaDB-Explorer-*-win-x64.msi`
- **macOS** — `NuvexaDB-Explorer-*-osx-*.pkg` (unsigned for now; always installs to `/Applications`)
- **Linux** — `nuvexadb-explorer_*_amd64.deb` (Debian/Ubuntu/Mint) and `nuvexadb-explorer-*-x86_64.rpm` (Fedora/RHEL/CentOS/openSUSE)

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

Desktop RIDs ship first (`osx-*`, `win-x64`, `linux-x64`). Android JNI uses Native AOT `linux-bionic-arm64` (not `android-arm64`). The .NET 10 SDK cannot PublishAot `ios-arm64`. ABI v2 adds catalog, transactions, and path-based GridFS. Details: [docs/bindings.md](docs/bindings.md). Language samples live inside each binding project.

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

## Complementary packages (Nuvyntra Labs)

- [Plugin.Maui.OfflineSync](https://www.nuget.org/packages/Plugin.Maui.OfflineSync) — offline-first sync
- [Plugin.Maui.JobQueue](https://www.nuget.org/packages/Plugin.Maui.JobQueue) — durable SQLite jobs
- [Plugin.Maui.FileVault](https://www.nuget.org/packages/Plugin.Maui.FileVault) — encrypted files
- [Plugin.Maui.SecureStoragePlus](https://www.nuget.org/packages/Plugin.Maui.SecureStoragePlus) — optional key storage
- [Plugin.Avalonia.MVVMExpress](https://www.nuget.org/packages/Plugin.Avalonia.MVVMExpress.Core) — Avalonia MVVM shell

## Docs

- [Explorer IDE](docs/explorer.md) — capability inventory (white paper source)
- [Change log](docs/changelog.md) — unreleased engine, Explorer, and bench notes
- [File format](docs/format.md)
- [Encryption](docs/encryption.md)
- [Engine sharing architecture](docs/architecture.md) — one engine, one C ABI, thin SDKs
- [Language bindings](docs/bindings.md)
- [NQL](docs/query.md)
- [Benchmarks](docs/benchmarks.md)
