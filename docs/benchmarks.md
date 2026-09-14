# Benchmarks

Project: `benches/Nuventra.NuvexaDB.Benchmarks`

```bash
dotnet run --project benches/Nuventra.NuvexaDB.Benchmarks -c Release -- --gate
```

Comparators: NuvexaDB (plain + encrypted), SQLite (`Microsoft.Data.Sqlite`), LiteDB.

1 crore (10,000,000) write / indexed read / complex query — **not** in CI. Writes `~/Downloads/nuvexa-1crore.nvx` and can take hours and several GB:

```bash
dotnet run --project benches/Nuventra.NuvexaDB.Benchmarks -c Release -- --crore
dotnet run --project benches/Nuventra.NuvexaDB.Benchmarks -c Release -- --scale 10000
```

Reuse the file on a later run (skip insert if `customers` already has enough rows). `--force` recreates. `--path /tmp/scale.nvx` overrides the file.

Indexed equality `find` + `limit` is an **IXSCAN** that stops after the limit (bounded prefix). Numeric ranges still walk the index because G17 keys are not numeric-order-preserving. See [changelog.md](changelog.md) for local 1,000-row and 1-crore timings.

Frozen v1 SLOs (enforced by `--gate`):

- Smoke (1000 inserts): fail if NuvexaDB is more than 5× slower than **both** SQLite and LiteDB
- Batch insert 10k: within 1.5× LiteDB
- Encrypted point get (cache-hot, median of 3): ≤ 30% slower than plaintext, or within +50ms (Windows CI noise floor on sub-20ms totals)
- Point get @ 100k docs (2000 lookups, median of 3 after warmup): within 3× SQLite PK, or within +50ms (macOS CI noise on a ~20ms SQLite baseline)
- Crash recovery: committed insert survives `Environment.FailFast` (see `tests/Nuventra.NuvexaDB.CrashHarness`)
