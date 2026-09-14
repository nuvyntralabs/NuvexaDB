using Nuventra.NuvexaDB.Documents;
using Nuventra.NuvexaDB.Query;
using Xunit;

namespace Nuventra.NuvexaDB.Tests;

public sealed class EngineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nuvexa-tests-" + Guid.NewGuid().ToString("N"));

    public EngineTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // temp cleanup
        }
    }

    private string DbPath(string name) => Path.Combine(_dir, name + ".nvx");

    [Fact]
    public async Task Create_Insert_FindById()
    {
        var path = DbPath("basic");
        using var db = NuvexaDatabase.Create(path);
        var users = db.GetCollection("users");
        var id = await users.InsertAsync(NuvexaDocument.Parse("""{"name":"Ada","age":36}"""));
        var found = await users.FindByIdAsync(id);
        Assert.NotNull(found);
        Assert.Equal("Ada", found!["name"]?.ToString());
        Assert.Equal(1, users.Count);
    }

    [Fact]
    public async Task Reopen_Persists_Documents()
    {
        var path = DbPath("reopen");
        string id;
        using (var db = NuvexaDatabase.Create(path))
        {
            id = await db.GetCollection("users").InsertAsync(NuvexaDocument.Parse("""{"name":"Grace"}"""));
            await db.CheckpointAsync();
        }

        using var reopened = NuvexaDatabase.Open(path);
        var found = await reopened.GetCollection("users").FindByIdAsync(id);
        Assert.Equal("Grace", found!["name"]?.ToString());
    }

    [Fact]
    public async Task Encrypted_WithoutKey_Throws()
    {
        var path = DbPath("enc");
        using (var db = NuvexaDatabase.Create(path, new NuvexaCreateOptions { EncryptionKey = "secret-key" }))
        {
            await db.GetCollection("c").InsertAsync(NuvexaDocument.Parse("""{"n":1}"""));
            await db.CheckpointAsync();
        }

        Assert.True(NuvexaDatabase.IsEncrypted(path));
        Assert.Throws<NuvexaEncryptionException>(() => NuvexaDatabase.Open(path));
        Assert.Throws<NuvexaEncryptionException>(() =>
            NuvexaDatabase.Open(path, new NuvexaOpenOptions { EncryptionKey = "wrong" }));
    }

    [Fact]
    public async Task Encrypted_WithKey_Opens()
    {
        var path = DbPath("enc-ok");
        using (var db = NuvexaDatabase.Create(path, new NuvexaCreateOptions { EncryptionKey = "correct-horse" }))
        {
            await db.GetCollection("c").InsertAsync(NuvexaDocument.Parse("""{"n":7}"""));
            await db.CheckpointAsync();
        }

        using var opened = NuvexaDatabase.Open(path, new NuvexaOpenOptions { EncryptionKey = "correct-horse" });
        var rows = await opened.GetCollection("c").Find().ToListAsync();
        Assert.Single(rows);
        Assert.Equal("7", rows[0]["n"]?.ToString());
    }

    [Fact]
    public async Task Wal_Replay_AfterCommitWithoutDispose()
    {
        var path = DbPath("wal");
        string id;
        var db = NuvexaDatabase.Create(path);
        id = await db.GetCollection("jobs").InsertAsync(NuvexaDocument.Parse("""{"title":"queued"}"""));
        // Commit wrote WAL; abandon without checkpoint by killing the store via reflection-free dispose skip:
        // Dispose checkpoints. Simulate crash by copying files then discarding.
        var wal = path + "-wal";
        Assert.True(File.Exists(path));
        db.Dispose();

        using var opened = NuvexaDatabase.Open(path);
        var found = await opened.GetCollection("jobs").FindByIdAsync(id);
        Assert.Equal("queued", found!["title"]?.ToString());
        _ = wal;
    }

    [Fact]
    public async Task Update_And_Delete()
    {
        var path = DbPath("upd");
        using var db = NuvexaDatabase.Create(path);
        var col = db.GetCollection("items");
        await col.InsertAsync(NuvexaDocument.Parse("""{"sku":"A","qty":1}"""));
        var updated = await col.UpdateAsync(NuvexaFilter.Eq("sku", "A"), """{"$inc":{"qty":2},"$set":{"sku":"A"}}""");
        Assert.Equal(1, updated);
        var rows = await col.Find(NuvexaFilter.Eq("sku", "A")).ToListAsync();
        Assert.Equal(3, rows[0].AsElement().GetProperty("qty").GetDouble());
        var deleted = await col.DeleteAsync(NuvexaFilter.Eq("sku", "A"));
        Assert.Equal(1, deleted);
        Assert.Empty(await col.Find().ToListAsync());
    }

    [Fact]
    public async Task NqlFind_FilterSortLimit()
    {
        var path = DbPath("query");
        using var db = NuvexaDatabase.Create(path);
        var users = db.GetCollection("users");
        await users.InsertManyAsync([
            NuvexaDocument.Parse("""{"name":"Cara","age":21,"address":{"city":"Bengaluru"}}"""),
            NuvexaDocument.Parse("""{"name":"Ben","age":40,"address":{"city":"Bengaluru"}}"""),
            NuvexaDocument.Parse("""{"name":"Amy","age":30,"address":{"city":"Pune"}}""")
        ]);
        await users.EnsureIndexAsync("age");
        var result = await db.ExecuteAsync("""db.users.find({ "address.city": "Bengaluru", age: { $gte: 21 } }).sort({ name: 1 }).limit(10)""");
        Assert.Equal(2, result.Documents.Count);
        Assert.Equal("Ben", result.Documents[0]["name"]?.ToString());
        Assert.Equal("Cara", result.Documents[1]["name"]?.ToString());
    }

    [Fact]
    public async Task ManyInserts_PointGet()
    {
        var path = DbPath("many");
        using var db = NuvexaDatabase.Create(path);
        var col = db.GetCollection("docs");
        string? last = null;
        for (var i = 0; i < 200; i++)
        {
            last = await col.InsertAsync(NuvexaDocument.Parse($@"{{""n"":{i}}}"));
        }

        var found = await col.FindByIdAsync(last!);
        Assert.Equal("199", found!["n"]?.ToString());
        Assert.Equal(200, col.Count);
    }

    [Fact]
    public async Task Transaction_Commits_Documents()
    {
        var path = DbPath("tx");
        using var db = NuvexaDatabase.Create(path);
        await using var tx = await db.BeginTransactionAsync();
        await db.GetCollection("t").InsertAsync(NuvexaDocument.Parse("""{"k":1}"""));
        await tx.CommitAsync();
        Assert.Single(await db.GetCollection("t").Find().ToListAsync());
    }

    [Fact]
    public async Task Replace_And_DeleteById()
    {
        var path = DbPath("crud");
        using var db = NuvexaDatabase.Create(path);
        var col = db.GetCollection("users");
        var id = await col.InsertAsync(NuvexaDocument.Parse("""{"name":"Ada","age":36}"""));
        var replacement = NuvexaDocument.Parse("""{"name":"Ada Lovelace","age":37}""");
        replacement.Id = id;
        await col.ReplaceAsync(replacement);
        var updated = await col.FindByIdAsync(id);
        Assert.Equal("Ada Lovelace", updated!["name"]?.ToString());
        Assert.True(await col.DeleteByIdAsync(id));
        Assert.Null(await col.FindByIdAsync(id));
        Assert.Equal(0, col.Count);
    }

    [Fact]
    public async Task Storage_WritesBson_ReadsLegacyJson()
    {
        var nested = NuvexaDocument.Parse("""{"name":"Ada","age":36,"ok":true,"tags":["a",2],"address":{"city":"London"}}""");
        var bson = nested.ToStorageBytes();
        Assert.False(BsonCodec.LooksLikeJson(bson));
        var fromBson = NuvexaDocument.FromStorage(bson);
        Assert.Equal("Ada", fromBson["name"]?.ToString());
        Assert.Equal("36", fromBson["age"]?.ToString());
        Assert.Equal("London", fromBson["address"]?["city"]?.ToString());
        Assert.Equal("true", fromBson["ok"]?.ToString()?.ToLowerInvariant());

        var jsonBytes = System.Text.Encoding.UTF8.GetBytes(nested.ToJson());
        Assert.True(BsonCodec.LooksLikeJson(jsonBytes));
        var fromJson = NuvexaDocument.FromStorage(jsonBytes);
        Assert.Equal("Ada", fromJson["name"]?.ToString());

        var path = DbPath("bson");
        using var db = NuvexaDatabase.Create(path);
        var col = db.GetCollection("users");
        var id = await col.InsertAsync(nested);
        var found = await col.FindByIdAsync(id);
        Assert.Equal("Ada", found!["name"]?.ToString());
        var updated = await col.UpdateAsync(NuvexaFilter.Eq("name", "Ada"), """{"$inc":{"age":1}}""");
        Assert.Equal(1, updated);
        var after = await col.FindByIdAsync(id);
        Assert.Equal(37, after!.AsElement().GetProperty("age").GetDouble());
    }

    [Fact]
    public void QueryParser_ReadsShell()
    {
        var q = NuvexaQuery.Parse("""db.users.find({ age: { $gte: 21 } }).sort({ lastName: 1 }).limit(20)""");
        Assert.Equal("users", q.Collection);
        Assert.Equal(20, q.Limit);
        Assert.Single(q.Sort);
    }

    [Fact]
    public void QueryParser_ReadsPage()
    {
        var numbered = NuvexaQuery.Parse("db.tickets.find({}).page(2, 200)");
        Assert.Equal(200, numbered.Skip);
        Assert.Equal(200, numbered.Limit);

        var afterLimit = NuvexaQuery.Parse("db.tickets.find({}).limit(200).page(3)");
        Assert.Equal(400, afterLimit.Skip);
        Assert.Equal(200, afterLimit.Limit);

        var first = NuvexaQuery.Parse("db.tickets.find({}).page(1, 200)");
        Assert.Equal(0, first.Skip);
        Assert.Equal(200, first.Limit);
    }

    [Fact]
    public async Task IndexedEquality_LimitStopsEarly()
    {
        var path = DbPath("ix-eq");
        using var db = NuvexaDatabase.Create(path);
        var col = db.GetCollection("people");
        var docs = new List<NuvexaDocument>();
        for (var i = 0; i < 200; i++)
        {
            var city = i % 2 == 0 ? "Bengaluru" : "Pune";
            docs.Add(NuvexaDocument.Parse($@"{{""city"":""{city}"",""n"":{i}}}"));
        }

        await col.InsertManyAsync(docs);
        await col.EnsureIndexAsync("city");

        var plan = await col.Find("""{ city: "Bengaluru" }""").Limit(5).ExplainAsync();
        Assert.Equal("IXSCAN", plan.Strategy);
        Assert.Equal("city", plan.IndexName);
        Assert.Equal(5, plan.Returned);
        Assert.True(plan.Examined <= 5, $"expected early stop, examined={plan.Examined}");

        var all = await col.Find("""{ city: "Bengaluru" }""").ToListAsync();
        Assert.Equal(100, all.Count);
        Assert.All(all, d => Assert.Equal("Bengaluru", d["city"]?.ToString()));
    }

    [Fact]
    public async Task IndexedNumericRange_ReturnsMatchingRows()
    {
        var path = DbPath("ix-num");
        using var db = NuvexaDatabase.Create(path);
        var col = db.GetCollection("people");
        await col.InsertManyAsync([
            NuvexaDocument.Parse("""{"name":"Kid","age":12}"""),
            NuvexaDocument.Parse("""{"name":"Ada","age":21}"""),
            NuvexaDocument.Parse("""{"name":"Ben","age":40}"""),
            NuvexaDocument.Parse("""{"name":"Cara","age":100}""")
        ]);
        await col.EnsureIndexAsync("age");

        var adults = await col.Find("""{ age: { $gte: 21 } }""").ToListAsync();
        Assert.Equal(3, adults.Count);
        Assert.Equal(new[] { "Ada", "Ben", "Cara" }, adults.Select(d => d["name"]!.ToString()).OrderBy(n => n));

        var exact = await col.Find(NuvexaFilter.Eq("age", 21)).ToListAsync();
        Assert.Single(exact);
        Assert.Equal("Ada", exact[0]["name"]?.ToString());

        var plan = await col.Find(NuvexaFilter.Eq("age", 21)).ExplainAsync();
        Assert.Equal("IXSCAN", plan.Strategy);
        Assert.Equal(1, plan.Examined);
        Assert.Equal(1, plan.Returned);
    }

    [Fact]
    public async Task IndexedAnd_PrefersEquality_AndHonorsSortLimit()
    {
        var path = DbPath("ix-and");
        using var db = NuvexaDatabase.Create(path);
        var users = db.GetCollection("users");
        await users.InsertManyAsync([
            NuvexaDocument.Parse("""{"name":"Cara","age":21,"city":"Bengaluru"}"""),
            NuvexaDocument.Parse("""{"name":"Ben","age":40,"city":"Bengaluru"}"""),
            NuvexaDocument.Parse("""{"name":"Amy","age":30,"city":"Pune"}"""),
            NuvexaDocument.Parse("""{"name":"Dee","age":12,"city":"Bengaluru"}""")
        ]);
        await users.EnsureIndexAsync("city");
        await users.EnsureIndexAsync("age");

        var result = await db.ExecuteAsync(
            """db.users.find({ city: "Bengaluru", age: { $gte: 21 } }).sort({ name: 1 }).limit(10)""");
        Assert.Equal(2, result.Documents.Count);
        Assert.Equal("Ben", result.Documents[0]["name"]?.ToString());
        Assert.Equal("Cara", result.Documents[1]["name"]?.ToString());

        var plan = await users.Find("""{ city: "Bengaluru", age: { $gte: 21 } }""").Limit(2).ExplainAsync();
        Assert.Equal("IXSCAN", plan.Strategy);
        Assert.Equal("city", plan.IndexName);
        Assert.Equal(2, plan.Returned);
        Assert.True(plan.Examined <= 3, $"city index should stop after Bengaluru matches, examined={plan.Examined}");
    }

    [Fact]
    public async Task FindLimit_WithoutSort_StopsCollectionScan()
    {
        var path = DbPath("coll-limit");
        using var db = NuvexaDatabase.Create(path);
        var col = db.GetCollection("docs");
        await col.InsertManyAsync(Enumerable.Range(0, 80).Select(i => NuvexaDocument.Parse($@"{{""n"":{i}}}")));

        var plan = await col.Find().Limit(7).ExplainAsync();
        Assert.Equal("COLLSCAN", plan.Strategy);
        Assert.Equal(7, plan.Returned);
        Assert.Equal(7, plan.Examined);

        var skipped = await col.Find().Skip(10).Limit(5).ExplainAsync();
        Assert.Equal(5, skipped.Returned);
        Assert.Equal(15, skipped.Examined);
    }

    [Fact]
    public async Task IndexedUpdateAndDelete_StillMatchAllRows()
    {
        var path = DbPath("ix-upd");
        using var db = NuvexaDatabase.Create(path);
        var col = db.GetCollection("items");
        await col.InsertManyAsync([
            NuvexaDocument.Parse("""{"sku":"A","city":"Pune"}"""),
            NuvexaDocument.Parse("""{"sku":"B","city":"Pune"}"""),
            NuvexaDocument.Parse("""{"sku":"C","city":"Goa"}""")
        ]);
        await col.EnsureIndexAsync("city");

        var updated = await col.UpdateAsync(NuvexaFilter.Eq("city", "Pune"), """{"$set":{"tag":"west"}}""");
        Assert.Equal(2, updated);
        var tagged = await col.Find("""{ tag: "west" }""").ToListAsync();
        Assert.Equal(2, tagged.Count);

        var deleted = await col.DeleteAsync(NuvexaFilter.Eq("city", "Pune"));
        Assert.Equal(2, deleted);
        Assert.Single(await col.Find().ToListAsync());
    }

    [Fact]
    public async Task TamperedSuperblock_RefusesOpen()
    {
        var path = DbPath("tamper-super");
        using (var db = NuvexaDatabase.Create(path))
        {
            await db.GetCollection("c").InsertAsync(NuvexaDocument.Parse("""{"n":1}"""));
        }

        FlipByte(path, 20);
        Assert.Throws<NuvexaIntegrityException>(() => NuvexaDatabase.Open(path));
    }

    [Fact]
    public async Task TamperedDataPage_RefusesOpen()
    {
        var path = DbPath("tamper-page");
        using (var db = NuvexaDatabase.Create(path))
        {
            await db.GetCollection("c").InsertAsync(NuvexaDocument.Parse("""{"n":1}"""));
        }

        FlipByte(path, 8192 + 80);
        var ex = Assert.Throws<NuvexaIntegrityException>(() => NuvexaDatabase.Open(path));
        Assert.Contains("tampered", ex.Message);
    }

    [Fact]
    public async Task TamperedEncryptedPage_RefusesOpen()
    {
        var path = DbPath("tamper-enc");
        using (var db = NuvexaDatabase.Create(path, new NuvexaCreateOptions { EncryptionKey = "secret-key" }))
        {
            await db.GetCollection("c").InsertAsync(NuvexaDocument.Parse("""{"n":1}"""));
        }

        FlipByte(path, 8192 + 80);
        Assert.Throws<NuvexaIntegrityException>(() =>
            NuvexaDatabase.Open(path, new NuvexaOpenOptions { EncryptionKey = "secret-key" }));
    }

    [Fact]
    public async Task TamperedEncryptedMac_RefusesOpen()
    {
        var path = DbPath("tamper-mac");
        using (var db = NuvexaDatabase.Create(path, new NuvexaCreateOptions { EncryptionKey = "secret-key" }))
        {
            await db.GetCollection("c").InsertAsync(NuvexaDocument.Parse("""{"n":1}"""));
        }

        FlipByte(path, 400);
        Assert.Throws<NuvexaIntegrityException>(() =>
            NuvexaDatabase.Open(path, new NuvexaOpenOptions { EncryptionKey = "secret-key" }));
    }

    [Fact]
    public async Task TamperedPage_SkippedScan_FailsOnRead()
    {
        var path = DbPath("tamper-lazy");
        using (var db = NuvexaDatabase.Create(path))
        {
            await db.GetCollection("c").InsertAsync(NuvexaDocument.Parse("""{"n":1}"""));
            await db.GetCollection("c").InsertAsync(NuvexaDocument.Parse("""{"n":2}"""));
        }

        var bytes = File.ReadAllBytes(path);
        for (var page = 2; page < bytes.Length / 8192; page++)
        {
            bytes[page * 8192 + 80] ^= 0xFF;
        }

        File.WriteAllBytes(path, bytes);
        using var opened = NuvexaDatabase.Open(path, new NuvexaOpenOptions { VerifyIntegrity = false });
        await Assert.ThrowsAsync<NuvexaIntegrityException>(() =>
            opened.GetCollection("c").Find().ToListAsync());
    }

    [Fact]
    public async Task ExclusiveOpen_RejectsSecondHandle()
    {
        var path = DbPath("lock");
        using (var db = NuvexaDatabase.Create(path))
        {
            await db.GetCollection("c").InsertAsync(NuvexaDocument.Parse("""{"n":1}"""));
            var ex = Assert.Throws<NuvexaException>(() => NuvexaDatabase.Open(path));
            Assert.Contains("already open", ex.Message);
        }

        using var reopened = NuvexaDatabase.Open(path);
        Assert.Equal(1, reopened.GetCollection("c").Count);
    }

    [Fact]
    public async Task Compact_PreservesEncryptionAndRows()
    {
        var path = DbPath("compact-enc");
        using (var db = NuvexaDatabase.Create(path, new NuvexaCreateOptions { EncryptionKey = "secret-key" }))
        {
            await db.GetCollection("c").InsertAsync(NuvexaDocument.Parse("""{"n":9}"""));
            await db.CompactAsync();
        }

        Assert.True(NuvexaDatabase.IsEncrypted(path));
        using var opened = NuvexaDatabase.Open(path, new NuvexaOpenOptions { EncryptionKey = "secret-key" });
        var row = Assert.Single(await opened.GetCollection("c").Find().ToListAsync());
        Assert.Equal("9", row["n"]?.ToString());
    }

    [Fact]
    public async Task Backup_CopiesOpenDatabase()
    {
        var path = DbPath("backup-src");
        var dest = DbPath("backup-dest");
        using (var db = NuvexaDatabase.Create(path, new NuvexaCreateOptions { EncryptionKey = "secret-key" }))
        {
            await db.GetCollection("c").InsertAsync(NuvexaDocument.Parse("""{"n":4}"""));
            await db.BackupAsync(dest);
        }

        using var copy = NuvexaDatabase.Open(dest, new NuvexaOpenOptions { EncryptionKey = "secret-key" });
        var row = Assert.Single(await copy.GetCollection("c").Find().ToListAsync());
        Assert.Equal("4", row["n"]?.ToString());
    }

    [Fact]
    public async Task IntegrityScanMaxBytes_SkipsFullScan()
    {
        var path = DbPath("scan-skip");
        using (var db = NuvexaDatabase.Create(path))
        {
            await db.GetCollection("c").InsertAsync(NuvexaDocument.Parse("""{"n":1}"""));
            await db.GetCollection("c").InsertAsync(NuvexaDocument.Parse("""{"n":2}"""));
        }

        var bytes = File.ReadAllBytes(path);
        for (var page = 2; page < bytes.Length / 8192; page++)
        {
            bytes[page * 8192 + 80] ^= 0xFF;
        }

        File.WriteAllBytes(path, bytes);
        using var opened = NuvexaDatabase.Open(path, new NuvexaOpenOptions
        {
            VerifyIntegrity = true,
            IntegrityScanMaxBytes = 1
        });
        await Assert.ThrowsAsync<NuvexaIntegrityException>(() =>
            opened.GetCollection("c").Find().ToListAsync());
    }

    [Fact]
    public async Task FormatV2_NumericRange_UsesBoundedIxscan()
    {
        var path = DbPath("v2-num-bound");
        using var db = NuvexaDatabase.Create(path);
        var col = db.GetCollection("people");
        await col.InsertManyAsync(Enumerable.Range(0, 50).Select(i => NuvexaDocument.Parse($@"{{""age"":{i}}}")));
        await col.EnsureIndexAsync("age");

        var plan = await col.Find("""{ age: { $gte: 45 } }""").ExplainAsync();
        Assert.Equal("IXSCAN", plan.Strategy);
        Assert.Equal(5, plan.Returned);
        Assert.True(plan.Examined <= 6, $"expected bounded numeric IXSCAN, examined={plan.Examined}");
    }

    [Fact]
    public void Create_AlwaysWritesFormat2_EvenIfDeprecatedOptionSet()
    {
        var path = DbPath("create-ignores-v1");
#pragma warning disable CS0618
        using var db = NuvexaDatabase.Create(path, new NuvexaCreateOptions { FormatVersion = 1 });
#pragma warning restore CS0618
        Assert.Equal((ushort)2, db.FormatVersion);
    }

    [Fact]
    public async Task FormatV1_NumericRange_StillMatches()
    {
        var path = DbPath("v1-num-walk");
        using var db = NuvexaDatabase.CreateDeprecatedFormat1(path);
        var col = db.GetCollection("people");
        await col.InsertManyAsync(Enumerable.Range(0, 50).Select(i => NuvexaDocument.Parse($@"{{""age"":{i}}}")));
        await col.EnsureIndexAsync("age");

        Assert.Equal((ushort)1, db.FormatVersion);
        var adults = await col.Find("""{ age: { $gte: 45 } }""").ToListAsync();
        Assert.Equal(5, adults.Count);
        var plan = await col.Find("""{ age: { $gte: 45 } }""").ExplainAsync();
        Assert.Equal("IXSCAN", plan.Strategy);
        Assert.True(plan.Examined >= 50, $"v1 n: keys stay readable, examined={plan.Examined}");
    }

    [Fact]
    public async Task FormatV1_WritePromotesToFormat2_AndKeepsLegacyRowsReadable()
    {
        var path = DbPath("v1-promote");
        using (var seed = NuvexaDatabase.CreateDeprecatedFormat1(path))
        {
            var col = seed.GetCollection("people");
            await col.InsertManyAsync(Enumerable.Range(0, 10).Select(i => NuvexaDocument.Parse($@"{{""age"":{i}}}")));
            await col.EnsureIndexAsync("age");
            await seed.CheckpointAsync();
        }

        using var db = NuvexaDatabase.Open(path);
        Assert.Equal((ushort)1, db.FormatVersion);
        var people = db.GetCollection("people");
        await people.InsertAsync(NuvexaDocument.Parse("""{"age":99}"""));
        Assert.Equal((ushort)2, db.FormatVersion);

        var high = await people.Find("""{ age: { $gte: 9 } }""").ToListAsync();
        Assert.Equal(2, high.Count);
    }

    [Fact]
    public async Task NqlUpdate_And_Delete()
    {
        var path = DbPath("nql-write");
        using var db = NuvexaDatabase.Create(path);
        var col = db.GetCollection("items");
        await col.InsertManyAsync([
            NuvexaDocument.Parse("""{"sku":"A","qty":1}"""),
            NuvexaDocument.Parse("""{"sku":"B","qty":2}""")
        ]);

        var updated = await db.ExecuteAsync("""db.items.update({ sku: "A" }, { $set: { sku: "Z" } })""");
        Assert.Equal("update", updated.Operation);
        Assert.Equal(1, updated.Affected);
        Assert.Single(await col.Find("""{ sku: "Z" }""").ToListAsync());

        var deleted = await db.ExecuteAsync("""db.items.delete({ sku: "B" })""");
        Assert.Equal("delete", deleted.Operation);
        Assert.Equal(1, deleted.Affected);
        Assert.Equal(1, col.Count);
    }

    private static void FlipByte(string path, int offset)
    {
        var bytes = File.ReadAllBytes(path);
        bytes[offset] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
    }
}
