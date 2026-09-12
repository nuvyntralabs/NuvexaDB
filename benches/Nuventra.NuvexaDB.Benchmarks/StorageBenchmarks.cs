using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using LiteDB;
using Microsoft.Data.Sqlite;
using Nuventra.NuvexaDB;

namespace Nuventra.NuvexaDB.Benchmarks;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Contains("--gate", StringComparer.OrdinalIgnoreCase))
        {
            return SloGate.Run();
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        return 0;
    }
}

[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 3)]
public class StorageBenchmarks
{
    private string _dir = "";

    [Params(1000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() => _dir = Directory.CreateTempSubdirectory("nuvexa-bench").FullName;

    [GlobalCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, true); } catch { /* ignore */ }
    }

    [Benchmark]
    public async Task Nuvexa_InsertAndGet()
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".nvx");
        using var db = NuvexaDatabase.Create(path);
        var col = db.GetCollection("docs");
        string? id = null;
        for (var i = 0; i < N; i++)
        {
            id = await col.InsertAsync(NuvexaDocument.Parse($@"{{""n"":{i},""name"":""user-{i}""}}"));
        }

        _ = await col.FindByIdAsync(id!);
    }

    [Benchmark]
    public async Task NuvexaEncrypted_InsertAndGet()
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".nvx");
        using var db = NuvexaDatabase.Create(path, new NuvexaCreateOptions { EncryptionKey = "bench-key" });
        var col = db.GetCollection("docs");
        string? id = null;
        for (var i = 0; i < N; i++)
        {
            id = await col.InsertAsync(NuvexaDocument.Parse($@"{{""n"":{i},""name"":""user-{i}""}}"));
        }

        _ = await col.FindByIdAsync(id!);
    }

    [Benchmark]
    public void Sqlite_InsertAndGet()
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".db");
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE docs (id TEXT PRIMARY KEY, payload TEXT);";
            cmd.ExecuteNonQuery();
        }

        string? id = null;
        using var tx = conn.BeginTransaction();
        for (var i = 0; i < N; i++)
        {
            id = i.ToString();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO docs(id, payload) VALUES ($id, $p);";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$p", $@"{{""n"":{i}}}");
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
        using var get = conn.CreateCommand();
        get.CommandText = "SELECT payload FROM docs WHERE id = $id;";
        get.Parameters.AddWithValue("$id", id);
        _ = get.ExecuteScalar();
    }

    [Benchmark]
    public void LiteDb_InsertAndGet()
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".litedb");
        using var db = new LiteDatabase(path);
        var col = db.GetCollection<BsonDocument>("docs");
        BsonValue? id = null;
        for (var i = 0; i < N; i++)
        {
            var doc = new BsonDocument { ["n"] = i, ["name"] = $"user-{i}" };
            col.Insert(doc);
            id = doc["_id"];
        }

        _ = col.FindById(id);
    }
}

internal static class SloGate
{
    public static int Run()
    {
        const int n = 1000;
        var dir = Directory.CreateTempSubdirectory("nuvexa-slo").FullName;
        try
        {
            var sqliteMs = Time(() =>
            {
                var path = Path.Combine(dir, "s.db");
                using var conn = new SqliteConnection($"Data Source={path}");
                conn.Open();
                using var create = conn.CreateCommand();
                create.CommandText = "CREATE TABLE docs (id TEXT PRIMARY KEY, payload TEXT);";
                create.ExecuteNonQuery();
                using var tx = conn.BeginTransaction();
                for (var i = 0; i < n; i++)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = "INSERT INTO docs(id, payload) VALUES ($id, $p);";
                    cmd.Parameters.AddWithValue("$id", i.ToString());
                    cmd.Parameters.AddWithValue("$p", "{}");
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
                using var get = conn.CreateCommand();
                get.CommandText = "SELECT payload FROM docs WHERE id = $id;";
                get.Parameters.AddWithValue("$id", (n - 1).ToString());
                _ = get.ExecuteScalar();
            });

            var liteMs = Time(() =>
            {
                var path = Path.Combine(dir, "s.litedb");
                using var db = new LiteDatabase(path);
                var col = db.GetCollection<BsonDocument>("docs");
                BsonValue? id = null;
                for (var i = 0; i < n; i++)
                {
                    var doc = new BsonDocument { ["n"] = i };
                    col.Insert(doc);
                    id = doc["_id"];
                }

                _ = col.FindById(id);
            });

            var nuvexaMs = Time(() =>
            {
                var path = Path.Combine(dir, "s.nvx");
                using var db = NuvexaDatabase.Create(path);
                var col = db.GetCollection("docs");
                string? id = null;
                col.InsertManyAsync(Enumerable.Range(0, n).Select(i => NuvexaDocument.Parse($@"{{""n"":{i}}}"))).GetAwaiter().GetResult();
                id = col.Find().ToListAsync().GetAwaiter().GetResult()[^1].Id;
                _ = col.FindByIdAsync(id).GetAwaiter().GetResult();
            });

            Console.WriteLine($"SLO smoke (n={n}): Nuvexa={nuvexaMs:F0}ms LiteDB={liteMs:F0}ms SQLite={sqliteMs:F0}ms");
            if (nuvexaMs > sqliteMs * 5 && nuvexaMs > liteMs * 5)
            {
                Console.Error.WriteLine("NuvexaDB is more than 5x slower than both SQLite and LiteDB. Revise the engine.");
                return 1;
            }

            return FrozenSlos(dir);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    private static int FrozenSlos(string dir)
    {
        const int insertN = 10_000;
        const int pointN = 100_000;
        const int lookups = 2_000;

        var litePath = Path.Combine(dir, "slo-10k.litedb");
        using (new LiteDatabase(litePath)) { /* create file */ }
        File.Delete(litePath);
        var lite10k = Time(() =>
        {
            using var db = new LiteDatabase(litePath);
            var col = db.GetCollection<BsonDocument>("docs");
            for (var i = 0; i < insertN; i++)
            {
                col.Insert(new BsonDocument { ["n"] = i });
            }
        });

        var nuvexaPlainPath = Path.Combine(dir, "slo-10k.nvx");
        using var nuvexaPlain = NuvexaDatabase.Create(nuvexaPlainPath);
        var nuvexa10k = Time(() =>
        {
            nuvexaPlain.GetCollection("docs").InsertManyAsync(
                Enumerable.Range(0, insertN).Select(i => NuvexaDocument.Parse($@"{{""n"":{i}}}"))).GetAwaiter().GetResult();
        });

        var nuvexaEncPath = Path.Combine(dir, "slo-10k-enc.nvx");
        using var nuvexaEnc = NuvexaDatabase.Create(nuvexaEncPath, new NuvexaCreateOptions { EncryptionKey = "slo-key" });
        var nuvexaEnc10k = Time(() =>
        {
            nuvexaEnc.GetCollection("docs").InsertManyAsync(
                Enumerable.Range(0, insertN).Select(i => NuvexaDocument.Parse($@"{{""n"":{i}}}"))).GetAwaiter().GetResult();
        });

        var nuvexa100kPath = Path.Combine(dir, "slo-100k.nvx");
        var nuvexa100kInsert = Time(() =>
        {
            using var db = NuvexaDatabase.Create(nuvexa100kPath);
            db.GetCollection("docs").InsertManyAsync(
                Enumerable.Range(0, pointN).Select(i => NuvexaDocument.Parse($@"{{""_id"":""{i}"",""n"":{i}}}"))).GetAwaiter().GetResult();
        });

        using var nuvexa100k = NuvexaDatabase.Open(nuvexa100kPath);
        var nuvexaCol = nuvexa100k.GetCollection("docs");
        _ = nuvexaCol.FindByIdAsync("0").GetAwaiter().GetResult();
        var nuvexaPoint = Time(() =>
        {
            for (var i = 0; i < lookups; i++)
            {
                _ = nuvexaCol.FindByIdAsync((i * (pointN / lookups)).ToString()).GetAwaiter().GetResult();
            }
        });

        var sqliteIds = Enumerable.Range(0, pointN).Select(i => i.ToString()).ToList();
        var sqlite100kPath = Path.Combine(dir, "slo-100k.db");
        Time(() =>
        {
            using var conn = new SqliteConnection($"Data Source={sqlite100kPath}");
            conn.Open();
            using var create = conn.CreateCommand();
            create.CommandText = "CREATE TABLE docs (id TEXT PRIMARY KEY, payload TEXT);";
            create.ExecuteNonQuery();
            using var tx = conn.BeginTransaction();
            for (var i = 0; i < pointN; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO docs(id, payload) VALUES ($id, $p);";
                cmd.Parameters.AddWithValue("$id", sqliteIds[i]);
                cmd.Parameters.AddWithValue("$p", "{}");
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        });

        using var sqliteConn = new SqliteConnection($"Data Source={sqlite100kPath}");
        sqliteConn.Open();
        using (var warm = sqliteConn.CreateCommand())
        {
            warm.CommandText = "SELECT payload FROM docs WHERE id = $id;";
            warm.Parameters.AddWithValue("$id", "0");
            _ = warm.ExecuteScalar();
        }

        var sqlitePoint = Time(() =>
        {
            for (var i = 0; i < lookups; i++)
            {
                using var get = sqliteConn.CreateCommand();
                get.CommandText = "SELECT payload FROM docs WHERE id = $id;";
                get.Parameters.AddWithValue("$id", sqliteIds[i * (pointN / lookups)]);
                _ = get.ExecuteScalar();
            }
        });

        var encLookups = 2_000;
        var plainCol = nuvexaPlain.GetCollection("docs");
        var encCol = nuvexaEnc.GetCollection("docs");
        var plainIds = plainCol.Find().Limit(encLookups).ToListAsync().GetAwaiter().GetResult().Select(d => d.Id).ToList();
        var encIds = encCol.Find().Limit(encLookups).ToListAsync().GetAwaiter().GetResult().Select(d => d.Id).ToList();
        _ = plainCol.FindByIdAsync(plainIds[0]).GetAwaiter().GetResult();
        _ = encCol.FindByIdAsync(encIds[0]).GetAwaiter().GetResult();
        var plainPoint = Time(() =>
        {
            foreach (var id in plainIds)
            {
                _ = plainCol.FindByIdAsync(id).GetAwaiter().GetResult();
            }
        });
        var encPoint = Time(() =>
        {
            foreach (var id in encIds)
            {
                _ = encCol.FindByIdAsync(id).GetAwaiter().GetResult();
            }
        });

        Console.WriteLine($"SLO 10k insert: Nuvexa={nuvexa10k:F0}ms LiteDB={lite10k:F0}ms Encrypted={nuvexaEnc10k:F0}ms");
        Console.WriteLine($"SLO encrypted point-get x{encLookups}: plain={plainPoint:F0}ms enc={encPoint:F0}ms");
        Console.WriteLine($"SLO 100k load: NuvexaInsert={nuvexa100kInsert:F0}ms  point-get x{lookups}: Nuvexa={nuvexaPoint:F0}ms SQLite={sqlitePoint:F0}ms");

        if (nuvexa10k > lite10k * 1.5)
        {
            Console.Error.WriteLine("10k insert: NuvexaDB exceeded 1.5× LiteDB.");
            return 1;
        }

        if (encPoint > plainPoint * 1.30)
        {
            Console.Error.WriteLine("Encrypted point get exceeded +30% vs plaintext.");
            return 1;
        }

        if (nuvexaPoint > sqlitePoint * 3)
        {
            Console.Error.WriteLine("100k-set point get exceeded 3× SQLite PK.");
            return 1;
        }

        return 0;
    }

    private static double Time(Action action)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        action();
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }
}
