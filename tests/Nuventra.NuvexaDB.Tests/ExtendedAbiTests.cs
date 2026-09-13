using System.Text.Json;
using Nuventra.NuvexaDB.Native;
using Xunit;

namespace Nuventra.NuvexaDB.Tests;

public sealed class ExtendedAbiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nuvexa-abi2-" + Guid.NewGuid().ToString("N"));

    public ExtendedAbiTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* temp */ }
    }

    private string Db(string name) => Path.Combine(_dir, name + ".nvx");

    [Fact]
    public void AbiVersion_IsTwo() => Assert.Equal(2, NuvexaAbi.AbiVersion);

    [Fact]
    public void Catalog_Count_Indexes_And_Rename()
    {
        Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Create(Db("catalog"), "key", out var handle));
        try
        {
            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Insert(handle, "users", """{"name":"Ada","age":36}""", out _));
            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.InsertMany(handle, "users", """[{"name":"Ben","age":12},{"name":"Cara","age":21}]""", out var ids));
            using (var parsed = JsonDocument.Parse(ids ?? "[]"))
            {
                Assert.Equal(2, parsed.RootElement.GetArrayLength());
            }

            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.EnsureIndex(handle, "users", "\"age\""));
            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.ListCollections(handle, out var collections));
            Assert.Contains("users", collections);

            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Count(handle, "users", """{"age":{"$gte":21}}""", out var adults));
            Assert.Equal(2, adults);

            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.ListIndexes(handle, "users", out var indexes));
            Assert.Contains("age", indexes);

            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.RenameCollection(handle, "users", "people"));
            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Count(handle, "people", "{}", out var all));
            Assert.Equal(3, all);

            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Stats(handle, out var stats));
            Assert.Contains("\"encrypted\":true", stats);
            Assert.Contains("\"documentCount\":3", stats);
        }
        finally
        {
            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Close(handle));
        }
    }

    [Fact]
    public void Transaction_Rollback_DropsUncommittedInsert()
    {
        Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Create(Db("tx"), null, out var handle));
        try
        {
            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Insert(handle, "t", """{"n":1}""", out _));
            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.BeginTransaction(handle));
            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Insert(handle, "t", """{"n":2}""", out _));
            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Rollback(handle));
            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Count(handle, "t", "{}", out var count));
            Assert.Equal(1, count);
        }
        finally
        {
            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Close(handle));
        }
    }

    [Fact]
    public void GridFs_Backup_Restore_And_Rekey()
    {
        var path = Db("fs");
        Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Create(path, "old-key", out var handle));
        try
        {
            var source = Path.Combine(_dir, "note.txt");
            File.WriteAllText(source, "hello-gridfs");
            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.FsUpload(handle, "note.txt", source, 4, out var id));
            Assert.False(string.IsNullOrWhiteSpace(id));

            var dest = Path.Combine(_dir, "out.txt");
            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.FsDownload(handle, id, dest, out var found));
            Assert.Equal(1, found);
            Assert.Equal("hello-gridfs", File.ReadAllText(dest));

            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.FsMetadata(handle, id, out var meta));
            Assert.Contains("note.txt", meta);

            var backup = Path.Combine(_dir, "copy.nvx");
            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Backup(handle, backup));
            Assert.True(File.Exists(backup));

            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.ChangeEncryptionKey(handle, "old-key", "new-key"));
            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Checkpoint(handle));
        }
        finally
        {
            Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Close(handle));
        }

        Assert.Equal(NuvexaAbi.Encryption, NuvexaAbi.Open(path, "old-key", out _));
        Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Open(path, "new-key", out var reopened));
        Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Close(reopened));

        var restored = Path.Combine(_dir, "restored.nvx");
        Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Restore(Path.Combine(_dir, "copy.nvx"), restored, 0));
        Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Open(restored, "old-key", out var fromBackup));
        Assert.Equal(NuvexaAbi.Ok, NuvexaAbi.Close(fromBackup));
    }
}
