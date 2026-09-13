using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Nuventra.NuvexaDB;

namespace Nuventra.NuvexaDB.Benchmarks;

/// <summary>
/// Opt-in large-file bench. Not part of <c>--gate</c>.
/// 1 crore = 10,000,000 documents.
/// </summary>
internal static class ScaleBench
{
    private static readonly string[] Cities =
    [
        "Bengaluru", "Mumbai", "Delhi", "Hyderabad", "Chennai",
        "Pune", "Kolkata", "Jaipur", "Ahmedabad", "Kochi"
    ];

    private static readonly string[] Statuses = ["active", "paid", "trial", "churned"];
    private static readonly string[] Segments = ["regular", "vip", "enterprise"];

    public static int Run(string[] args)
    {
        var n = ParseCount(args);
        var force = args.Contains("--force", StringComparer.OrdinalIgnoreCase);
        var path = ParsePath(args, n);
        const int batch = 10_000;
        const int cacheMb = 256;

        Console.WriteLine($"NuvexaDB scale bench  n={n:N0}  ({n / 10_000_000d:0.##} crore)");
        Console.WriteLine($"File: {path}");
        Console.WriteLine($"Batch={batch:N0}  cache={cacheMb} MB  force={force}");

        NuvexaDatabase db;
        var wrote = false;
        if (File.Exists(path) && !force)
        {
            db = NuvexaDatabase.Open(path, new NuvexaOpenOptions { CacheSizeMb = cacheMb, CheckpointThreshold = 4096 });
            var existing = db.GetCollection("customers").Count;
            Console.WriteLine($"Opened existing file. customers={existing:N0}");
            if (existing < n)
            {
                db.Dispose();
                Console.Error.WriteLine($"File has {existing:N0} customers; need {n:N0}. Pass --force to recreate.");
                return 1;
            }
        }
        else
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            var wal = path + "-wal";
            if (File.Exists(wal))
            {
                File.Delete(wal);
            }

            db = NuvexaDatabase.Create(path, new NuvexaCreateOptions { CacheSizeMb = cacheMb, CheckpointThreshold = 4096 });
            wrote = true;
        }

        using (db)
        {
            var customers = db.GetCollection("customers");
            if (wrote)
            {
                var insertMs = Time(() => BulkInsert(db, customers, n, batch));
                var docsPerSec = insertMs > 0 ? n / (insertMs / 1000.0) : n;
                Console.WriteLine($"WRITE  InsertMany {n:N0} customers: {Format(insertMs)}  ({docsPerSec:N0} docs/s)");
            }
            else
            {
                Console.WriteLine("WRITE  skipped (reusing file)");
            }

            EnsureCities(db);
            EnsureTickets(db, customers);

            foreach (var field in new[] { "city", "status", "age" })
            {
                if (HasIndex(customers, field))
                {
                    Console.WriteLine($"INDEX  {field}: already present");
                    continue;
                }

                var ms = Time(() => customers.EnsureIndexAsync(field).GetAwaiter().GetResult());
                Console.WriteLine($"INDEX  EnsureIndex({field}) on {customers.Count:N0} rows: {Format(ms)}");
            }

            var stats = db.GetStats();
            Console.WriteLine($"FILE   {stats.FileBytes:N0} bytes  wal={stats.WalBytes:N0}  pages={stats.PageCount:N0}  docs={stats.DocumentCount:N0}");

            RunReads(customers, n);
            RunQueries(db, customers, n);
        }

        Console.WriteLine("Scale bench finished.");
        return 0;
    }

    private static void BulkInsert(NuvexaDatabase db, NuvexaCollection customers, int n, int batch)
    {
        var sw = Stopwatch.StartNew();
        for (var start = 0; start < n; start += batch)
        {
            var take = Math.Min(batch, n - start);
            var docs = new NuvexaDocument[take];
            for (var i = 0; i < take; i++)
            {
                docs[i] = Customer(start + i);
            }

            customers.InsertManyAsync(docs).GetAwaiter().GetResult();
            var done = start + take;
            if (done == n || done % 100_000 == 0)
            {
                db.CheckpointAsync().GetAwaiter().GetResult();
                var elapsed = sw.Elapsed.TotalSeconds;
                var rate = elapsed > 0 ? done / elapsed : 0;
                var eta = rate > 0 ? TimeSpan.FromSeconds((n - done) / rate) : TimeSpan.Zero;
                Console.WriteLine($"  insert {done:N0}/{n:N0}  {rate:N0} docs/s  ETA {eta:hh\\:mm\\:ss}");
            }
        }
    }

    private static void RunReads(NuvexaCollection customers, int n)
    {
        var lastId = Id(n - 1);
        var warm = Time(() => _ = customers.FindByIdAsync(lastId).GetAwaiter().GetResult());
        Console.WriteLine($"READ   warm FindById({lastId}): {Format(warm)}");

        var lookups = Math.Min(2_000, n);
        var step = Math.Max(1, n / lookups);
        var point = Time(() =>
        {
            for (var i = 0; i < lookups; i++)
            {
                _ = customers.FindByIdAsync(Id(i * step)).GetAwaiter().GetResult();
            }
        });
        Console.WriteLine($"READ   point-get x{lookups:N0} by _id: {Format(point)}  ({lookups / Math.Max(point / 1000.0, 0.001):N0} gets/s)");
    }

    private static void RunQueries(NuvexaDatabase db, NuvexaCollection customers, int n)
    {
        Explain(customers, """{ "city": "Bengaluru" }""", "city equality");

        TimedQuery("QUERY  find city=Bengaluru limit 50", () =>
            customers.Find("""{ "city": "Bengaluru" }""").Limit(50).ToListAsync().GetAwaiter().GetResult());

        TimedQuery("QUERY  find age>=60 sort age limit 50", () =>
            customers.Find("""{ "age": { "$gte": 60 } }""").Sort("age").Limit(50).ToListAsync().GetAwaiter().GetResult());

        TimedQuery("QUERY  city+status+age (Bengaluru, paid, age>=21) limit 50", () =>
            customers.Find("""{ "city": "Bengaluru", "status": "paid", "age": { "$gte": 21 } }""")
                .Sort("age", ascending: false)
                .Limit(50)
                .ToListAsync()
                .GetAwaiter()
                .GetResult());

        TimedQuery("QUERY  execute find city+limit", () =>
            db.ExecuteAsync("""db.customers.find({ city: "Bengaluru" }).sort({ age: -1 }).limit(50)""")
                .GetAwaiter()
                .GetResult()
                .Documents);

        if (n <= 250_000)
        {
            TimedQuery("QUERY  email $regex @example.com limit 20 (COLLSCAN)", () =>
                customers.Find("""{ "email": { "$regex": "@example.com" } }""")
                    .Limit(20)
                    .ToListAsync()
                    .GetAwaiter()
                    .GetResult());
        }
        else
        {
            Console.WriteLine("QUERY  email $regex skipped at this scale (full collection scan). Use --scale 250000 or less to include it.");
        }

        TimedQuery("QUERY  tickets $lookup cities (small collections)", () =>
            db.ExecuteAsync("""
                db.tickets.aggregate([
                  { $match: { city: "Bengaluru" } },
                  { $lookup: { from: "cities", localField: "city", foreignField: "_id", as: "cityDoc" } },
                  { $limit: 25 }
                ])
                """)
                .GetAwaiter()
                .GetResult()
                .Documents);

        Console.WriteLine("QUERY  aggregate $count on customers is skipped at 1 crore (loads the collection into memory).");
    }

    private static void Explain(NuvexaCollection customers, string filter, string label)
    {
        var plan = customers.Find(filter).Limit(50).ExplainAsync().GetAwaiter().GetResult();
        Console.WriteLine($"EXPLAIN {label}: {plan.Strategy} index={plan.IndexName ?? "none"} examined={plan.Examined} returned={plan.Returned}");
    }

    private static void TimedQuery(string label, Func<List<NuvexaDocument>> run)
    {
        List<NuvexaDocument> rows = [];
        var ms = Time(() => rows = run());
        Console.WriteLine($"{label}: {Format(ms)}  rows={rows.Count}");
    }

    private static void EnsureCities(NuvexaDatabase db)
    {
        var col = db.GetCollection("cities");
        if (col.Count > 0)
        {
            return;
        }

        foreach (var city in Cities)
        {
            col.InsertAsync(new NuvexaDocument(new JsonObject
            {
                ["_id"] = city,
                ["country"] = "IN",
                ["tier"] = city is "Bengaluru" or "Mumbai" or "Delhi" ? 1 : 2
            })).GetAwaiter().GetResult();
        }
    }

    private static void EnsureTickets(NuvexaDatabase db, NuvexaCollection customers)
    {
        var tickets = db.GetCollection("tickets");
        if (tickets.Count > 0)
        {
            return;
        }

        var take = 5_000;
        for (var i = 0; i < take; i++)
        {
            tickets.InsertAsync(new NuvexaDocument(new JsonObject
            {
                ["_id"] = $"t-{i:D5}",
                ["customerId"] = Id(i),
                ["city"] = Cities[i % Cities.Length],
                ["status"] = Statuses[i % Statuses.Length]
            })).GetAwaiter().GetResult();
        }

        _ = customers;
    }

    private static bool HasIndex(NuvexaCollection customers, string field) =>
        customers.ListIndexesAsync().GetAwaiter().GetResult().Any(i => i.FieldPath == field);

    private static NuvexaDocument Customer(int i)
    {
        var city = Cities[i % Cities.Length];
        var status = Statuses[(i / Cities.Length) % Statuses.Length];
        var segment = Segments[(i / 3) % Segments.Length];
        return new NuvexaDocument(new JsonObject
        {
            ["_id"] = Id(i),
            ["name"] = "User " + i.ToString(CultureInfo.InvariantCulture),
            ["email"] = "user" + i.ToString(CultureInfo.InvariantCulture) + "@example.com",
            ["age"] = 18 + (i % 53),
            ["status"] = status,
            ["city"] = city,
            ["segment"] = segment,
            ["balance"] = i % 10_000,
            ["createdAt"] = "2024-01-01T00:00:00"
        });
    }

    private static string Id(int i) => "c-" + i.ToString("D8", CultureInfo.InvariantCulture);

    private static int ParseCount(string[] args)
    {
        if (args.Contains("--crore", StringComparer.OrdinalIgnoreCase))
        {
            return 10_000_000;
        }

        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--scale", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i + 1], NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var n)
                && n > 0)
            {
                return n;
            }
        }

        return 10_000_000;
    }

    private static string ParsePath(string[] args, int n)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--path", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(args[i + 1]);
            }
        }

        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(downloads);
        var name = n == 10_000_000 ? "nuvexa-1crore.nvx" : $"nuvexa-scale-{n}.nvx";
        return Path.Combine(downloads, name);
    }

    private static double Time(Action action)
    {
        var sw = Stopwatch.StartNew();
        action();
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    private static string Format(double ms) =>
        ms >= 60_000
            ? $"{TimeSpan.FromMilliseconds(ms):hh\\:mm\\:ss} ({ms:N0} ms)"
            : $"{ms:N0} ms";
}
