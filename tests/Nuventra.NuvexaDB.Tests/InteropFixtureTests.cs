using System.Text.Json;
using Nuventra.NuvexaDB.Native;
using Xunit;

namespace Nuventra.NuvexaDB.Tests;

public sealed class InteropFixtureTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nuvexa-interop-" + Guid.NewGuid().ToString("N"));

    public InteropFixtureTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* temp */ }
    }

    [Fact]
    public async Task ManagedEngine_MatchesGoldenCases()
    {
        var fixture = InteropCases.Load();
        var path = Path.Combine(_dir, "golden.nvx");
        using (var db = NuvexaDatabase.Create(path, new NuvexaCreateOptions { EncryptionKey = fixture.Key }))
        {
            await SeedAsync(db, fixture);
            await AssertCasesAsync(db, fixture);
            await db.CheckpointAsync();
        }

        Assert.True(NuvexaDatabase.IsEncrypted(path));
        Assert.Throws<NuvexaEncryptionException>(() => NuvexaDatabase.Open(path));

        using var opened = NuvexaDatabase.Open(path, new NuvexaOpenOptions { EncryptionKey = fixture.Key });
        await AssertCasesAsync(opened, fixture);
    }

    [Fact]
    public async Task NativeAbi_MatchesGoldenCases()
    {
        var fixture = InteropCases.Load();
        var path = Path.Combine(_dir, "abi.nvx");
        Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Create(path, fixture.Key, out var handle));
        try
        {
            await SeedAbiAsync(handle, fixture);
            AssertCasesAbi(handle, fixture);
        }
        finally
        {
            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Close(handle));
        }

        Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.IsEncrypted(path, out var encrypted));
        Assert.Equal(1, encrypted);
        Assert.Equal(NuvexaAbi.Encryption, NuvexaAbi.Open(path, null, out _));

        Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Open(path, fixture.Key, out var reopened));
        try
        {
            AssertCasesAbi(reopened, fixture);
        }
        finally
        {
            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Close(reopened));
        }
    }

    internal static async Task SeedAsync(NuvexaDatabase db, InteropCases fixture)
    {
        var col = db.GetCollection(fixture.Collection);
        foreach (var doc in fixture.Documents)
        {
            await col.InsertAsync(NuvexaDocument.Parse(doc.GetRawText()));
        }

        foreach (var fields in fixture.Indexes)
        {
            if (fields.Count == 1)
            {
                await col.EnsureIndexAsync(fields[0]);
            }
            else
            {
                await col.EnsureIndexAsync(fields);
            }
        }
    }

    internal static async Task AssertCasesAsync(NuvexaDatabase db, InteropCases fixture)
    {
        foreach (var query in fixture.Cases)
        {
            var result = await db.ExecuteAsync(query.Nql);
            var names = result.Documents.Select(d => d["name"]?.ToString() ?? "").ToList();
            Assert.Equal(query.ExpectNames, names);
        }
    }

    private static Task SeedAbiAsync(nint handle, InteropCases fixture)
    {
        foreach (var doc in fixture.Documents)
        {
            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Insert(handle, fixture.Collection, doc.GetRawText(), out _));
        }

        foreach (var fields in fixture.Indexes)
        {
            var json = fields.Count == 1
                ? JsonSerializer.Serialize(fields[0])
                : JsonSerializer.Serialize(fields);
            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.EnsureIndex(handle, fixture.Collection, json));
        }

        return Task.CompletedTask;
    }

    private static void AssertCasesAbi(nint handle, InteropCases fixture)
    {
        foreach (var query in fixture.Cases)
        {
            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Execute(handle, query.Nql, out var json));
            using var parsed = JsonDocument.Parse(json ?? "[]");
            var names = parsed.RootElement.EnumerateArray()
                .Select(el => el.GetProperty("name").GetString() ?? "")
                .ToList();
            Assert.Equal(query.ExpectNames, names);
        }
    }
}

public sealed class InteropCases
{
    public required string Key { get; init; }
    public required string Collection { get; init; }
    public required List<JsonElement> Documents { get; init; }
    public required List<List<string>> Indexes { get; init; }
    public required List<InteropQueryCase> Cases { get; init; }

    public static InteropCases Load()
    {
        var path = FindCases();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        return new InteropCases
        {
            Key = root.GetProperty("key").GetString() ?? "",
            Collection = root.GetProperty("collection").GetString() ?? "",
            Documents = root.GetProperty("documents").EnumerateArray().Select(e => e.Clone()).ToList(),
            Indexes = root.GetProperty("indexes").EnumerateArray()
                .Select(arr => arr.EnumerateArray().Select(f => f.GetString() ?? "").ToList())
                .ToList(),
            Cases = root.GetProperty("cases").EnumerateArray().Select(c => new InteropQueryCase
            {
                Name = c.GetProperty("name").GetString() ?? "",
                Nql = c.GetProperty("nql").GetString() ?? "",
                ExpectNames = c.GetProperty("expectNames").EnumerateArray()
                    .Select(n => n.GetString() ?? "").ToList()
            }).ToList()
        };
    }

    private static string FindCases()
    {
        var start = new DirectoryInfo(AppContext.BaseDirectory);
        for (var dir = start; dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "interop", "cases.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var copied = Path.Combine(dir.FullName, "interop", "cases.json");
            if (File.Exists(copied))
            {
                return copied;
            }
        }

        throw new FileNotFoundException("tests/interop/cases.json was not found.");
    }
}

public sealed class InteropQueryCase
{
    public required string Name { get; init; }
    public required string Nql { get; init; }
    public required List<string> ExpectNames { get; init; }
}
