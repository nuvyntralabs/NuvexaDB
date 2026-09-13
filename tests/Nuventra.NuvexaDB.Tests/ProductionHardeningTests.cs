using Nuventra.NuvexaDB.Query;
using Xunit;

namespace Nuventra.NuvexaDB.Tests;

public sealed class ProductionHardeningTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nuvexa-prod-" + Guid.NewGuid().ToString("N"));

    public ProductionHardeningTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp */ }
    }

    private string DbPath(string name) => Path.Combine(_dir, name + ".nvx");

    [Fact]
    public async Task ConcurrentFinds_DoNotThrow()
    {
        using var db = NuvexaDatabase.Create(DbPath("concurrent"));
        var col = db.GetCollection("docs");
        await col.InsertManyAsync(Enumerable.Range(0, 40).Select(i => NuvexaDocument.Parse($@"{{""n"":{i}}}")));

        var tasks = Enumerable.Range(0, 8)
            .Select(_ => col.Find(NuvexaFilter.Parse("{ n: { $gte: 0 } }")).ToListAsync())
            .ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.All(results, rows => Assert.Equal(40, rows.Count));
    }

    [Fact]
    public async Task Transaction_Rollback_DiscardsInserts()
    {
        using var db = NuvexaDatabase.Create(DbPath("rollback"));
        await db.GetCollection("t").InsertAsync(NuvexaDocument.Parse("""{"k":0}"""));
        await using (var tx = await db.BeginTransactionAsync())
        {
            await db.GetCollection("t").InsertAsync(NuvexaDocument.Parse("""{"k":1}"""));
            await tx.RollbackAsync();
        }

        var rows = await db.GetCollection("t").Find().ToListAsync();
        Assert.Single(rows);
        Assert.Equal("0", rows[0]["k"]?.ToString());
    }

    [Fact]
    public async Task Transaction_Dispose_RollsBack()
    {
        using var db = NuvexaDatabase.Create(DbPath("tx-dispose"));
        await using (var tx = await db.BeginTransactionAsync())
        {
            await db.GetCollection("t").InsertAsync(NuvexaDocument.Parse("""{"k":1}"""));
        }

        Assert.Empty(await db.GetCollection("t").Find().ToListAsync());
    }

    [Fact]
    public async Task CompoundIndex_EqualityUsesIxscan()
    {
        using var db = NuvexaDatabase.Create(DbPath("compound"));
        var col = db.GetCollection("orders");
        await col.InsertManyAsync([
            NuvexaDocument.Parse("""{"city":"Pune","status":"open","n":1}"""),
            NuvexaDocument.Parse("""{"city":"Pune","status":"done","n":2}"""),
            NuvexaDocument.Parse("""{"city":"Goa","status":"open","n":3}""")
        ]);
        await col.EnsureIndexAsync(["city", "status"]);

        var listed = await col.ListIndexesAsync();
        Assert.Contains(listed, i => i.FieldPath == "city,status" && i.FieldPaths.Count == 2);

        var rows = await col.Find(NuvexaFilter.Parse("""{ city: "Pune", status: "open" }""")).ToListAsync();
        Assert.Single(rows);
        Assert.Equal("1", rows[0]["n"]?.ToString());

        var plan = await col.Find(NuvexaFilter.Parse("""{ city: "Pune", status: "open" }""")).ExplainAsync();
        Assert.Equal("IXSCAN", plan.Strategy);
    }

    [Fact]
    public async Task Compact_KeepsHandleUsable()
    {
        var path = DbPath("compact-live");
        using var db = NuvexaDatabase.Create(path);
        var col = db.GetCollection("c");
        await col.InsertManyAsync(Enumerable.Range(0, 20).Select(i => NuvexaDocument.Parse($@"{{""n"":{i}}}")));
        await db.CompactAsync();
        Assert.Equal(20, db.GetCollection("c").Count);
        Assert.Equal(20, (await col.Find().ToListAsync()).Count);
        await col.InsertAsync(NuvexaDocument.Parse("""{"n":20}"""));
        Assert.Equal(21, db.GetCollection("c").Count);
    }

    [Fact]
    public async Task Restore_CopiesBackup()
    {
        var src = DbPath("restore-src");
        var bak = DbPath("restore-bak");
        var dest = DbPath("restore-dest");
        using (var db = NuvexaDatabase.Create(src, new NuvexaCreateOptions { EncryptionKey = "secret-key" }))
        {
            await db.GetCollection("c").InsertAsync(NuvexaDocument.Parse("""{"n":7}"""));
            await db.BackupAsync(bak);
        }

        await NuvexaDatabase.RestoreAsync(bak, dest);
        using var opened = NuvexaDatabase.Open(dest, new NuvexaOpenOptions { EncryptionKey = "secret-key" });
        var row = Assert.Single(await opened.GetCollection("c").Find().ToListAsync());
        Assert.Equal("7", row["n"]?.ToString());
    }

    [Fact]
    public async Task Aggregate_Group_Sum()
    {
        using var db = NuvexaDatabase.Create(DbPath("group"));
        var col = db.GetCollection("orders");
        await col.InsertManyAsync([
            NuvexaDocument.Parse("""{"city":"Pune","total":10}"""),
            NuvexaDocument.Parse("""{"city":"Pune","total":5}"""),
            NuvexaDocument.Parse("""{"city":"Goa","total":3}""")
        ]);
        var result = await db.ExecuteAsync("""db.orders.aggregate([{ $group: { _id: "$city", n: { $sum: 1 }, total: { $sum: "$total" } } }])""");
        Assert.Equal(2, result.Documents.Count);
        var pune = result.Documents.Single(d => d["_id"]?.ToString() == "Pune");
        Assert.Equal("2", pune["n"]?.ToString());
        Assert.Equal("15", pune["total"]?.ToString());
    }

    [Fact]
    public async Task Lookup_RespectsCap()
    {
        using var db = NuvexaDatabase.Create(DbPath("lookup-cap"));
        db.LookupMaxDocuments = 1;
        await db.GetCollection("users").InsertManyAsync([
            NuvexaDocument.Parse("""{"_id":"u1","name":"Ada"}"""),
            NuvexaDocument.Parse("""{"_id":"u2","name":"Ben"}""")
        ]);
        await db.GetCollection("orders").InsertAsync(NuvexaDocument.Parse("""{"userId":"u1"}"""));
        var ex = await Assert.ThrowsAsync<NuvexaException>(() =>
            db.ExecuteAsync("""db.orders.aggregate([{ $lookup: { from: "users", localField: "userId", foreignField: "_id", as: "user" } }])"""));
        Assert.Contains("cap", ex.Message);
        db.LookupMaxDocuments = 0;
        var joined = await db.ExecuteAsync("""db.orders.aggregate([{ $lookup: { from: "users", localField: "userId", foreignField: "_id", as: "user" } }])""");
        Assert.Single(joined.Documents);
    }

    [Fact]
    public async Task Find_ToAsyncEnumerable_MatchesToList()
    {
        using var db = NuvexaDatabase.Create(DbPath("async-enum"));
        var col = db.GetCollection("docs");
        await col.InsertManyAsync(Enumerable.Range(0, 10).Select(i => NuvexaDocument.Parse($@"{{""n"":{i}}}")));
        var listed = await col.Find().Sort("n").ToListAsync();
        var streamed = new List<NuvexaDocument>();
        await foreach (var doc in col.Find().Sort("n").ToAsyncEnumerable())
        {
            streamed.Add(doc);
        }

        Assert.Equal(listed.Select(d => d["n"]?.ToString()), streamed.Select(d => d["n"]?.ToString()));
    }
}
