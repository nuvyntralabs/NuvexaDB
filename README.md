# NuvexaDB

Embedded, Mongo-inspired NoSQL for **.NET** and **.NET MAUI**. One portable binary **`.nvx`** file, AES-256-GCM encryption, and a desktop explorer for Windows, macOS, and Linux.

**Package:** `Nuventra.NuvexaDB`  
**Author:** Niladri Prasad Padhy / Nuventra  
**License:** MIT  
**Product name:** NuvexaDB (this repo). The MauiEssentials catalog is published under **Nuvyntra** Labs — the spellings are intentional.

## Why NuvexaDB

MauiEssentials already uses SQLite as a **local cache / outbox** ([JobQueue](https://www.nuget.org/packages/Plugin.Maui.JobQueue), [OfflineSync](https://www.nuget.org/packages/Plugin.Maui.OfflineSync)). Those stay the right tools for durable jobs and sync. NuvexaDB is the general-purpose **document** file: collections, Mongo-like queries, encryption, and an IDE.

Use **SQLite** when you need SQL joins or an existing sqlite-net model. Use **NuvexaDB** when you want documents, a single `.nvx` application file, and fail-closed encryption.

## Install

```bash
dotnet add package Nuventra.NuvexaDB
```

Do not publish this package from a local clone. CI on `main` / tags pushes to nuget.org and GitHub Packages.

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

var rows = await db.ExecuteAsync("""db.users.find({ age: { $gte: 21 } }).sort({ name: 1 }).limit(20)""");
```

Opening an encrypted file **without** a key throws `NuvexaEncryptionException` (fail-closed). The Explorer, Visual Studio editor, and VS Code / Cursor editor prompt for the key.

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
```

Operators: `$eq $ne $gt $gte $lt $lte $in $nin $and $or $exists $regex`. Updates: `$set $unset $inc $push $pull`.

Aggregation: `$match $project $sort $skip $limit $count $lookup`. Typed LINQ: `GetCollection<T>().Where(p => p.Age >= 21)`. Files: `db.Files.UploadAsync` / `DownloadAsync` (`fs.files` / `fs.chunks`).

## Product layers

| Layer | Project | Role |
| --- | --- | --- |
| **Database core** | `src/Nuventra.NuvexaDB/Engine`, `Encryption`, `Query` | Pages, WAL, B+tree, AES-256-GCM, filters, aggregation |
| **Access library** | `Nuventra.NuvexaDB` | `NuvexaDatabase` / `NuvexaCollection` / `AddNuvexaDB` for apps |
| **DB Explorer IDE** | `Nuventra.NuvexaDB.Explorer` | Avalonia + [Plugin.Avalonia.MVVMExpress](https://www.nuget.org/packages/Plugin.Avalonia.MVVMExpress) on Windows, macOS, and Linux |
| **Editor extensions** | `Nuventra.NuvexaDB.VSCode`, `Nuventra.NuvexaDB.VisualStudio` | Custom editor / tool window over the same `ExplorerSession` |

Supporting: `Nuventra.NuvexaDB.Tools` (session + grid cache), `nuvexa` CLI (used by VS Code).

## Samples

- `samples/Console` — create, encrypt, fail-closed open, query
- `samples/Maui` — Android / iOS / Mac Catalyst / Windows app-data `.nvx`
- `samples/Avalonia` — desktop file + query

## Benchmarks

```bash
dotnet run --project benches/Nuventra.NuvexaDB.Benchmarks -c Release -- --gate
```

`--gate` also freezes 10k insert (≤ 1.5× LiteDB), encrypted point-get (≤ +30%), and 100k-set point-get (≤ 3× SQLite). File association scripts live in `src/Nuventra.NuvexaDB.Explorer/packaging/`.

## Complementary packages (Nuvyntra Labs)

- [Plugin.Maui.OfflineSync](https://www.nuget.org/packages/Plugin.Maui.OfflineSync) — offline-first sync
- [Plugin.Maui.JobQueue](https://www.nuget.org/packages/Plugin.Maui.JobQueue) — durable SQLite jobs
- [Plugin.Maui.FileVault](https://www.nuget.org/packages/Plugin.Maui.FileVault) — encrypted files
- [Plugin.Maui.SecureStoragePlus](https://www.nuget.org/packages/Plugin.Maui.SecureStoragePlus) — optional key storage
- [Plugin.Avalonia.MVVMExpress](https://www.nuget.org/packages/Plugin.Avalonia.MVVMExpress.Core) — Avalonia MVVM shell

## Docs

- [File format](docs/format.md)
- [Encryption](docs/encryption.md)
- [Query](docs/query.md)
- [Benchmarks](docs/benchmarks.md)
