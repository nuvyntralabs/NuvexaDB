# Benchmarks

Project: `benches/Nuventra.NuvexaDB.Benchmarks`

```bash
dotnet run --project benches/Nuventra.NuvexaDB.Benchmarks -c Release -- --gate
```

Comparators: NuvexaDB (plain + encrypted), SQLite (`Microsoft.Data.Sqlite`), LiteDB.

Frozen v1 SLOs (enforced by `--gate`):

- Smoke (1000 inserts): fail if NuvexaDB is more than 5× slower than **both** SQLite and LiteDB
- Batch insert 10k: within 1.5× LiteDB
- Encrypted point get: ≤ 30% slower than plaintext
- Point get @ 100k docs (2000 lookups): within 3× SQLite PK
- Crash recovery: committed insert survives `Environment.FailFast` (see `tests/Nuventra.NuvexaDB.CrashHarness`)
