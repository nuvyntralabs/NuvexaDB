using Nuventra.NuvexaDB.Tools;

namespace Nuventra.NuvexaDB.VisualStudio;

/// <summary>
/// In-process explorer used by the Visual Studio tool window / custom editor.
/// Bind <see cref="Tree"/> and <see cref="Rows"/> to a WPF or WinForms pane.
/// </summary>
public sealed class NuvexaToolWindow : IAsyncDisposable
{
    public ExplorerSession Session { get; } = new();
    public IReadOnlyList<ExplorerNode> Tree { get; private set; } = [];
    public IReadOnlyList<DocumentRow> Rows { get; private set; } = [];
    public string Status { get; private set; } = "Closed.";
    public string? SelectedCollection { get; private set; }
    public DocumentRow? SelectedRow { get; private set; }
    public string DocumentJson => SelectedRow?.Json ?? "";

    public async Task<string> OpenOrPromptAsync(string path, Func<string, Task<string?>> askKey)
    {
        if (ExplorerSession.PeekEncrypted(path))
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var key = await askKey("This .nvx file is encrypted. Enter the encryption key.").ConfigureAwait(false);
                if (string.IsNullOrEmpty(key))
                {
                    Status = "cancelled";
                    return "cancelled";
                }

                try
                {
                    await Session.OpenAsync(path, key).ConfigureAwait(false);
                    await RefreshAsync().ConfigureAwait(false);
                    return "ok";
                }
                catch (NuvexaEncryptionException)
                {
                    // retry
                }
            }

            Status = "failed";
            return "failed";
        }

        await Session.OpenAsync(path, null).ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
        return "ok";
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Tree = await Session.LoadTreeAsync(cancellationToken).ConfigureAwait(false);
        var stats = Session.Stats();
        Status = $"{stats.Path} docs={stats.DocumentCount} encrypted={stats.Encrypted}";
    }

    public async Task SelectNodeAsync(ExplorerNode node, CancellationToken cancellationToken = default)
    {
        if (node.Kind == "collection")
        {
            await LoadCollectionAsync(node.Name, cancellationToken).ConfigureAwait(false);
            return;
        }

        var parent = Tree.FirstOrDefault(n => n.Children.Contains(node));
        if (parent is not null)
        {
            SelectedCollection = parent.Name;
            Status = $"Index {node.Name} on {parent.Name}";
        }
    }

    public async Task LoadCollectionAsync(string collection, CancellationToken cancellationToken = default)
    {
        SelectedCollection = collection;
        var docs = await Session.ListAsync(collection, 500, cancellationToken).ConfigureAwait(false);
        Rows = Session.ToGrid(docs);
        SelectedRow = Rows.Count == 0 ? null : Rows[0];
        Status = $"{collection}: {Rows.Count} document(s).";
    }

    public async Task QueryAsync(string text, CancellationToken cancellationToken = default)
    {
        var docs = await Session.QueryAsync(text, cancellationToken).ConfigureAwait(false);
        Rows = Session.ToGrid(docs);
        SelectedRow = Rows.Count == 0 ? null : Rows[0];
        Status = $"{docs.Count} document(s).";
    }

    public void SelectRow(DocumentRow? row) => SelectedRow = row;

    public Task ImportJsonAsync(string collection, string jsonArray, CancellationToken cancellationToken = default) =>
        Session.ImportJsonAsync(collection, jsonArray, cancellationToken);

    public string ExportJson() => ExplorerSession.ExportJson(
        Rows.Select(r => NuvexaDocument.Parse(r.Json)));

    public Task CompactAsync(CancellationToken cancellationToken = default) => Session.CompactAsync(cancellationToken);

    public Task ChangeEncryptionKeyAsync(string currentKey, string nextKey, CancellationToken cancellationToken = default) =>
        Session.ChangeEncryptionKeyAsync(currentKey, nextKey, cancellationToken);

    public ValueTask DisposeAsync() => Session.DisposeAsync();
}
