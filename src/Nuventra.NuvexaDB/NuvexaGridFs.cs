using System.Text.Json.Nodes;

namespace Nuventra.NuvexaDB;

/// <summary>GridFS-style file store in <c>fs.files</c> / <c>fs.chunks</c>.</summary>
public sealed class NuvexaGridFs
{
    public const int DefaultChunkSize = 255 * 1024;
    private readonly NuvexaDatabase _db;

    internal NuvexaGridFs(NuvexaDatabase db) => _db = db;

    public async Task<string> UploadAsync(string fileName, Stream content, int chunkSize = DefaultChunkSize, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        chunkSize = Math.Clamp(chunkSize, 1024, 8 * 1024 * 1024);
        var files = _db.GetCollection("fs.files");
        var chunks = _db.GetCollection("fs.chunks");
        var id = NuvexaObjectId.NewId();
        var buffer = new byte[chunkSize];
        var n = 0;
        var length = 0L;
        int read;
        while ((read = await content.ReadAsync(buffer.AsMemory(0, chunkSize), cancellationToken).ConfigureAwait(false)) > 0)
        {
            length += read;
            var chunk = new NuvexaDocument(new JsonObject
            {
                ["files_id"] = id,
                ["n"] = n,
                ["data"] = Convert.ToBase64String(buffer.AsSpan(0, read).ToArray())
            });
            await chunks.InsertAsync(chunk, cancellationToken).ConfigureAwait(false);
            n++;
        }

        var file = new NuvexaDocument(new JsonObject
        {
            ["_id"] = id,
            ["filename"] = fileName,
            ["length"] = length,
            ["chunkSize"] = chunkSize,
            ["uploadDate"] = DateTimeOffset.UtcNow.ToString("O")
        });
        await files.InsertAsync(file, cancellationToken).ConfigureAwait(false);
        return id;
    }

    public async Task<bool> DownloadAsync(string fileId, Stream destination, CancellationToken cancellationToken = default)
    {
        var meta = await _db.GetCollection("fs.files").FindByIdAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (meta is null)
        {
            return false;
        }

        var parts = await _db.GetCollection("fs.chunks")
            .Find(Query.NuvexaFilter.Eq("files_id", fileId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var part in parts.OrderBy(p => p.AsElement().GetProperty("n").GetInt32()))
        {
            var bytes = Convert.FromBase64String(part["data"]?.ToString() ?? "");
            await destination.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    public Task<NuvexaDocument?> GetMetadataAsync(string fileId, CancellationToken cancellationToken = default) =>
        _db.GetCollection("fs.files").FindByIdAsync(fileId, cancellationToken);
}
