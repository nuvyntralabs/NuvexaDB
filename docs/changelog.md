# NuvexaDB change log

Working-tree notes for unreleased work. Publishing still happens only through CI.

## Unreleased — 14 September 2026

New files write **format v2**: order-preserving numeric index keys (`d:`) so `$gte` / `$lte` can IXSCAN, and a 32-byte WAL header that records page size. Format v1 files stay readable. Data Studio can edit an existing table definition, run NQL `update` / `delete`, complete NQL with Ctrl+Space, and open an aggregation builder plus visual explain. Visual Studio and VS Code browse grids are editable (`nuvexa replace`).

CI publishes iOS Native AOT shared libraries (`ios-arm64` + `iossimulator-arm64`, `PublishAotUsingRuntimePack`) and packs `Nuvexa.xcframework` as `NuvexaDB-Native-iOS`. Simulator ABI cases and React Native iOS link that framework. Linux ARM64 (`ubuntu-24.04-arm`) and Windows ARM64 (`windows-11-arm`) publish the C ABI and Data Studio installers and run host ABI tests on that CPU.

## Unreleased — 13 September 2026

WAL, encryption, and public collection APIs are unchanged. New documents are stored as **BSON**. Existing JSON pages still read (mixed files are valid). Existing files including `~/Downloads/nuvexa-1crore.nvx` stay readable.

### On-disk documents

Data pages write BSON (typed binary). `NuvexaDocument.Parse` / `ToJson` / Explorer stay JSON. A payload that looks like `{…}` is treated as legacy UTF-8 JSON. Index keys are unchanged (`s:` / `n:` / `b:`).

### Engine find path

Indexed `find` no longer materializes the whole secondary index, then applies `skip` / `limit`.

- Equality and string ranges use B+tree `lo` / `hi` (prefix inclusive, successor exclusive).
- `$and` still applies the full filter. When several fields are indexed it prefers equality, then a string range, then a numeric range.
- Numeric ranges still walk the index. G17 index keys are not numeric-order-preserving, so tight number bounds would skip valid rows. `Matches` still applies the real compare (`age >= 21` stays correct).
- Without `sort`, `skip` / `limit` apply while scanning (`limit == 0` still means “all matches” for update/delete).
- With `sort`, matches are collected then sorted (needed when the sort field is not the index).

Explain `Examined` is the number of documents actually visited.

### NQL (Nuvexa Query Language)

The supported query language is **NQL**. `db.<collection>.find({ … })` already had `.sort().skip().limit().project()`.

Added **`.page(page)`** and **`.page(page, size)`** (1-based):

| Query | Meaning |
| --- | --- |
| `db.tickets.find({}).page(2, 200)` | Same as `.skip(200).limit(200)` |
| `db.tickets.find({}).limit(200).page(3)` | Skip 400, keep limit 200 |

Invalid `page` (missing, zero, or negative) throws `NuvexaException`.

### File integrity

Open refuses a tampered `.nvx` (`NuvexaIntegrityException`). Superblock CRC is checked on every open. Encrypted files also HMAC-SHA256 the superblock with the DEK (written on create / checkpoint). Allocated pages are scanned by default (`NuvexaOpenOptions.VerifyIntegrity`, CRC32 or AES-GCM). Set `VerifyIntegrity = false` only for very large files; a bad page still fails when it is first read. The engine does not repair or overwrite a failed file.

Existing files without an HMAC still open if CRC and pages verify. The next checkpoint writes the HMAC.

### Production hardening (no format change)

- Exclusive file lock (`FileShare.None`). A second `Open` / `Create` on the same path throws `NuvexaException`.
- `CompactAsync` writes an encrypted dest when the source is encrypted (uses the key from Open/Create).
- `BackupAsync` checkpoints and copies the `.nvx` through the open stream (does not `File.Copy` a locked file).
- WAL superblock records get the same DEK HMAC as page 0. Legacy zero-MAC WAL records still replay.
- Full integrity scan skipped when the file is larger than `IntegrityScanMaxBytes` (default 64 MiB). Superblock checks still run. Set `0` to always scan.
- Explorer / session `CreateAsync` uses `NuvexaCreateOptions.ForDesktop` (64 MiB Argon2). App `Create()` stays 16 MiB. Existing files keep stored KDF params.
- On-disk format version remains **1**.
- Concurrent `Find` / reads on one handle; writes remain exclusive. Page cache and file I/O are locked.
- `NuvexaTransaction.RollbackAsync`. `BeginTransactionAsync` checkpoints first so abort can restore the last durable snapshot. `await using` without `CommitAsync` now rolls back (was implicit commit). Callers that already `CommitAsync` are unchanged.
- Compound indexes: `EnsureIndexAsync(["city", "status"])`. Single-field `EnsureIndexAsync("age")` and existing index keys are unchanged.
- `CompactAsync` streams documents in 256-row batches and **keeps the handle open** (swaps the file in place). Explorer session no longer closes and reopens after compact.
- `$group` (`$sum` / `$min` / `$max` / `$avg` / `$first`). `$lookup` refuses a foreign collection larger than `LookupMaxDocuments` (default 100 000; `0` disables). Small `$lookup` tests unchanged.
- `Find().ToAsyncEnumerable()`, `NuvexaDatabase.RestoreAsync`, `nuvexa restore`, public `NuvexaLimits`.

### Explorer IDE

- Desktop app name is **Nuvexa Data Studio**, with a window / dock / installer icon.
- Theme follows the OS (light / dark). **View → Theme** can pin System, Light, or Dark. Chrome uses navy/cyan cards, Inter, a compact toolbar, and resizable Structure / Browse / NQL panes.
- Database Structure right pane lists the selected collection’s columns and can add, edit, or delete them. **Observed fields** samples up to 200 documents (types, coverage, examples).
- Browse: JSON **Tree** tab, clone record, multi-select delete, find-in-page, click-to-sort this page, field/operator **Build filter** (still runs through `BrowsePageAsync`).
- File: CSV import/export and query-result CSV. Tools: backup, restore, database properties. View: **Read-only**. Drag-and-drop a `.nvx` to open. Close reports a leftover WAL file when present.
- NQL: named **Saved** queries (`explorer-saved-queries.json`).
- VS Code / Visual Studio Browse Data: **Build filter**, find-in-page, JSON **Tree**, click-to-sort this page. Still browse-only. `BrowseFilterBuilder` / `JsonDocumentTree` live on `Nuventra.NuvexaDB.Tools`.
- **About**: Data Studio **Help → About Nuvexa**; VS Code About tab + `NuvexaDB: About`; Visual Studio About tab. Copy lives on `NuvexaAbout`.

- **Structure tree counts** update after insert / delete / browse reload (`customers  (6)`). The node is updated in place so the tree does not collapse.
- **Browse Data pagination**: 200 rows per page, **Previous** / **Next**, status `Showing A–B of T` / `Page X of Y`. A new unfiltered record opens the last page. Deleting the last row on a page steps back one page. Small collections still show `N record(s).` with no pager.
- **NQL** tab: Examples include **Page**. `.skip().limit()` and `.page()` both run through `ExecuteAsync`.
- Browse paging and query examples live on `ExplorerSession.BrowsePageAsync` / `ExplorerQuerySample` so Visual Studio and VS Code stay in sync with the desktop IDE.

### Samples

Language samples live only inside each binding project (JVM `src/sampleJava` / `src/sampleKotlin`, Flutter `examples/sample.dart`, and the other SDK `examples/` folders). Catalog folders `samples/Java`, `Kotlin`, `Flutter`, and the other language sample trees were removed. .NET samples: `Console`, `Maui`, `Avalonia`, `Wpf`, `WinUI`, and `Uno`. The `samples/Relationships` generator was removed.

### Language bindings (ABI v2)

C ABI `nuvexa_abi_version()` is **2**. Added catalog (`list` / `drop` / `rename` collections), `insert_many`, `count`, index list/drop, `stats`, checkpoint / backup / compact / restore, rekey, transactions, and path-based GridFS. Existing SDKs (Kotlin/Java, Swift, Flutter, React Native) expose the new symbols. New thin SDKs: Python (ctypes), Node (`@nuventra/nuvexadb-node`), Go (cgo), C++ (`nuvexa.hpp`). LINQ stays .NET-only. Fixtures: `tests/interop/cases.json` plus `ExtendedAbiTests`. Docs: `docs/architecture.md`, `docs/bindings.md`.

### Visual Studio and VS Code

- VS Code / Cursor: **Open Database** / **Close Database**, collapsible collection + Columns tree, **Browse Data** and **Execute Query** tabs. Browse-only (no create / edit / delete).
- CLI: `nuvexa browse`, `nuvexa samples`, `nuvexa explain`; `find` accepts `--skip` / `--limit` / `--page`.

### CI

GitHub Actions runs the C# unit tests first, then builds every library, IDE, and extension. Test result / coverage artifacts are not uploaded. Everyday `main` / PR downloads stay on the run Artifacts list. **The Releases page is updated only when a `v*` tag is pushed and every CI job on that run succeeded.** A failed job skips the release. If GitHub already has a published `v<Version>` matching `Directory.Build.props`, **the rest of CI does not start** (PRs still build). CI **Version alignment** requires NuGet, every language SDK, Data Studio, VS Code, and Visual Studio to share `Directory.Build.props` `<Version>` (`1.0.0` now; Android `versionCode` is `10000`). **nuget.org, GitHub Packages, and NuGet key/version validation stay commented out** until multi-host publishing is decided. Language SDK jobs (`NuvexaDB-Java`, `Python`, `Node`, `React-Native`, `Go`, `Cpp`, `Swift`, `Flutter`, `Android`) wait for the matching `NuvexaDB-Native-<rid>` artifact and run that SDK’s interop suite before packing. Android native is `linux-bionic-arm64` (JNI `.so`). iOS CI publishes `ios-arm64` + `iossimulator-arm64` with `PublishAotUsingRuntimePack` and packs `Nuvexa.xcframework`. The Android AAR and React Native modules run the golden fixtures before they assemble.

### Tests

- Indexed equality + early `limit`, numeric range correctness, `$and` + sort + limit, collection-scan `limit` / `skip`, indexed update/delete.
- `FindAsync` skip/limit pages; `LoadTree` count after insert.
- Query parser `.page(2, 200)` / `.limit(200).page(3)`.
- Explorer tree assertion looks under the **Indexes** group.
- Interop fixtures (`tests/interop/cases.json`) run through the managed engine, `NuvexaAbi`, Kotlin, Swift, Flutter, React Native, Python, Node, Go, and C++.
- ABI v2 catalog / transaction / GridFS cases in `ExtendedAbiTests`.

### Scale bench (local, not `--gate`)

```bash
dotnet run --project benches/Nuventra.NuvexaDB.Benchmarks -c Release -- --scale 1000 --force
dotnet run --project benches/Nuventra.NuvexaDB.Benchmarks -c Release -- --crore
```

**1,000 customers** (`~/Downloads/nuvexa-scale-1000.nvx`), 13 September 2026:

| Step | Result |
| --- | --- |
| InsertMany 1,000 | 93 ms (~10,800 docs/s) |
| Index city / status / age | 39 / 19 / 19 ms |
| Point-get × 1,000 | 6 ms |
| `city = Bengaluru` limit 50 | 1 ms, **IXSCAN examined=50** |
| city + status + age limit 50 | 1 ms, 22 rows |

**1 crore** (earlier run on this machine): write ~5.5 min; compound `city+status+age` limit 50 was a 38 s collection scan before the find-path change. Re-run `--crore` without `--force` to measure IXSCAN on that file.

### Compatibility (intentionally unchanged)

- Page size, WAL records, encryption wrap, index key encoding (`n:` / `s:` / `b:`).
- Insert / replace / delete / catalog persist (no deferred catalog write inside `InsertMany`).
- Aggregate `$lookup` / `$count` still load the source collection (do not `$lookup` into 10M customers).
