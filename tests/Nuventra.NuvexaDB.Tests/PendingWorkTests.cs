using System.Diagnostics;
using Nuventra.NuvexaDB.Tools;
using Nuventra.NuvexaDB.VisualStudio;
using Xunit;

namespace Nuventra.NuvexaDB.Tests;

public sealed class PendingWorkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nuvexa-pending-" + Guid.NewGuid().ToString("N"));

    public PendingWorkTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* temp */ }
    }

    private string Db(string name) => Path.Combine(_dir, name + ".nvx");

    [Fact]
    public async Task CrossCollection_Transaction()
    {
        using var db = NuvexaDatabase.Create(Db("tx2"));
        await using var tx = await db.BeginTransactionAsync();
        await db.GetCollection("orders").InsertAsync(NuvexaDocument.Parse("""{"userId":"u1","total":9}"""));
        await db.GetCollection("users").InsertAsync(NuvexaDocument.Parse("""{"_id":"u1","name":"Ada"}"""));
        await tx.CommitAsync();
        Assert.Single(await db.GetCollection("orders").Find().ToListAsync());
        Assert.Single(await db.GetCollection("users").Find().ToListAsync());
    }

    [Fact]
    public async Task Lookup_And_Count()
    {
        using var db = NuvexaDatabase.Create(Db("agg"));
        await db.GetCollection("users").InsertAsync(NuvexaDocument.Parse("""{"_id":"u1","name":"Ada"}"""));
        await db.GetCollection("orders").InsertAsync(NuvexaDocument.Parse("""{"userId":"u1","total":9}"""));
        var joined = await db.ExecuteAsync("""db.orders.aggregate([{ $lookup: { from: "users", localField: "userId", foreignField: "_id", as: "user" } }])""");
        Assert.Single(joined.Documents);
        var counted = await db.ExecuteAsync("""db.orders.aggregate([{ $count: "n" }])""");
        Assert.Equal("1", counted.Documents[0]["n"]?.ToString());
    }

    [Fact]
    public async Task Linq_Where()
    {
        using var db = NuvexaDatabase.Create(Db("linq"));
        var col = db.GetCollection<Person>("people");
        await col.InsertManyAsync([new Person { Name = "Ada", Age = 36 }, new Person { Name = "Ben", Age = 12 }]);
        var adults = await col.ToListAsync(p => p.Age >= 21);
        Assert.Single(adults);
        Assert.Equal("Ada", adults[0].Name);
        var named = await col.Where(p => p.Name == "Ben").ToListAsync();
        Assert.Single(named);
    }

    [Fact]
    public async Task ExplorerSession_TreeAndGrid()
    {
        var path = Db("tree");
        using (var db = NuvexaDatabase.Create(path))
        {
            var users = db.GetCollection("users");
            await users.InsertAsync(NuvexaDocument.Parse("""{"name":"Ada","age":36}"""));
            await users.EnsureIndexAsync("age");
        }

        await using var session = new ExplorerSession();
        await session.OpenAsync(path, null);
        var tree = await session.LoadTreeAsync();
        Assert.Contains(tree, n => n.Name == "users" && n.Children.Any(c =>
            c.Name == "Indexes" && c.Children.Any(i => i.Name == "age")));
        var rows = session.ToGrid(await session.ListAsync("users"));
        Assert.Single(rows);
        Assert.Contains("Ada", rows[0].Cells["name"]);
        await session.CompactAsync();
        Assert.True(session.IsOpen);
        Assert.Single(await session.ListAsync("users"));
    }

    [Fact]
    public async Task VisualStudio_ToolWindow()
    {
        var path = Db("vs");
        using (var db = NuvexaDatabase.Create(path))
        {
            await db.GetCollection("users").InsertAsync(NuvexaDocument.Parse("""{"name":"Ada"}"""));
        }

        await using var window = new NuvexaToolWindow();
        Assert.Equal("ok", await window.OpenOrPromptAsync(path, _ => Task.FromResult<string?>(null)));
        var users = Assert.Single(window.Tree, n => n.Name == "users");
        Assert.Equal("users  (1)", users.Caption);
        await window.LoadCollectionAsync("users");
        Assert.Single(window.Rows);
        Assert.Contains("Ada", window.ExportJson());
        window.ApplyGridFind("no-such-row");
        Assert.Empty(window.Rows);
        window.ApplyGridFind("");
        Assert.Single(window.Rows);
        await window.ApplyBuiltFilterAsync("name", "equals", "Ada");
        Assert.Equal("name: Ada", window.BrowseFilter);
        Assert.Single(window.Rows);
        Assert.NotEmpty(window.DocumentTree);
        Assert.False(window.HasNextPage);
        Assert.Contains("examined=", window.Explain);
        Assert.Equal("db.users.find({}).page(2, 200)", window.ResolveSample(window.QuerySamples.Single(s => s.Title == "Page")));
        await window.QueryAsync("db.users.find({}).limit(10)");
        Assert.Equal("users", window.SelectedCollection);
        Assert.Single(window.Rows);
        Assert.Single(window.QueryRows);
        Assert.Contains("Ada", window.ExportQueryJson());
        Assert.Contains("examined=", window.QueryExplain);
    }

    [Fact]
    public async Task GridFs_RoundTrip()
    {
        using var db = NuvexaDatabase.Create(Db("fs"));
        var payload = "hello-gridfs"u8.ToArray();
        string id;
        await using (var input = new MemoryStream(payload))
        {
            id = await db.Files.UploadAsync("note.txt", input, chunkSize: 4);
        }

        await using var output = new MemoryStream();
        Assert.True(await db.Files.DownloadAsync(id, output));
        Assert.Equal(payload, output.ToArray());
    }

    [Fact]
    public async Task ChangeEncryptionKey()
    {
        var path = Db("rekey");
        using (var db = NuvexaDatabase.Create(path, new NuvexaCreateOptions { EncryptionKey = "old-key" }))
        {
            await db.GetCollection("c").InsertAsync(NuvexaDocument.Parse("""{"n":1}"""));
            await db.ChangeEncryptionKeyAsync("old-key", "new-key");
            await db.CheckpointAsync();
        }

        Assert.Throws<NuvexaEncryptionException>(() =>
            NuvexaDatabase.Open(path, new NuvexaOpenOptions { EncryptionKey = "old-key" }));
        using var opened = NuvexaDatabase.Open(path, new NuvexaOpenOptions { EncryptionKey = "new-key" });
        Assert.Single(await opened.GetCollection("c").Find().ToListAsync());
    }

    [Fact]
    public async Task ProcessKill_RecoversCommittedInsert()
    {
        var path = Db("kill");
        var harness = Path.Combine(AppContext.BaseDirectory, "Nuventra.NuvexaDB.CrashHarness.dll");
        Assert.True(File.Exists(harness), "Crash harness was not copied to the test output.");

        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{harness}\" \"{path}\"",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        });
        Assert.NotNull(proc);
        await proc!.WaitForExitAsync();
        var idPath = path + ".id";
        Assert.True(File.Exists(idPath));
        var id = (await File.ReadAllTextAsync(idPath)).Trim();
        NuvexaDatabase? db = null;
        for (var attempt = 0; attempt < 8 && db is null; attempt++)
        {
            try
            {
                db = NuvexaDatabase.Open(path);
            }
            catch (IOException) when (attempt < 7)
            {
                await Task.Delay(50);
            }
        }

        Assert.NotNull(db);
        using (db)
        {
            var found = await db!.GetCollection("crash").FindByIdAsync(id);
            Assert.NotNull(found);
            Assert.Equal("true", found!["ok"]?.ToString()?.ToLowerInvariant());
        }
    }

    private sealed class Person
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }
}
