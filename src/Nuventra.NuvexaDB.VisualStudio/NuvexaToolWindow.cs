using Nuventra.NuvexaDB;
using Nuventra.NuvexaDB.Tools;

namespace Nuventra.NuvexaDB.VisualStudio;

/// <summary>
/// In-process explorer used by the Visual Studio tool window / custom editor.
/// Bind <see cref="Tree"/>, <see cref="Rows"/> (Browse Data), and <see cref="QueryRows"/> (NQL) to a WPF or WinForms pane.
/// Same browse, query, and sample APIs as the Avalonia Explorer.
/// </summary>
public sealed class NuvexaToolWindow : IAsyncDisposable
{
    public ExplorerSession Session { get; } = new();
    public IReadOnlyList<ExplorerNode> Tree { get; private set; } = [];
    public IReadOnlyList<DocumentRow> Rows { get; private set; } = [];
    public IReadOnlyList<DocumentRow> QueryRows { get; private set; } = [];
    public IReadOnlyList<ExplorerQuerySample> QuerySamples { get; } = ExplorerQuerySample.All;
    public string Status { get; private set; } = "Closed.";
    public string Explain { get; private set; } = "";
    public string QueryExplain { get; private set; } = "";
    public string QueryStatus { get; private set; } = "";
    public string BrowseFilter { get; set; } = "";
    public string BrowsePageText { get; private set; } = "";
    public string BrowseStatus { get; private set; } = "";
    public bool HasPreviousPage { get; private set; }
    public bool HasNextPage { get; private set; }
    public int BrowsePage { get; private set; }
    public string? SelectedCollection { get; private set; }
    public DocumentRow? SelectedRow { get; private set; }
    public DocumentRow? SelectedQueryRow { get; private set; }
    public string DocumentJson => SelectedRow?.Json ?? "";
    public string QueryDocumentJson => SelectedQueryRow?.Json ?? "";

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
            BrowsePage = 0;
            await LoadCollectionAsync(node.Name, cancellationToken).ConfigureAwait(false);
            return;
        }

        var collection = FindCollectionName(node);
        if (collection is null)
        {
            return;
        }

        SelectedCollection = collection;
        Status = node.Kind switch
        {
            "field" => $"Column {node.Name} on {collection}",
            "index" => $"Index {node.Name} on {collection}",
            "group" => $"{node.Name} in {collection}",
            _ => node.Caption
        };
    }

    public async Task LoadCollectionAsync(string collection, CancellationToken cancellationToken = default)
    {
        SelectedCollection = collection;
        var page = await Session.BrowsePageAsync(
            collection,
            BrowseFilter,
            BrowsePage,
            BrowsePageResult.DefaultPageSize,
            cancellationToken).ConfigureAwait(false);
        ApplyBrowsePage(page);
        RefreshCaptions();
    }

    public Task ApplyBrowseFilterAsync(string? filter, CancellationToken cancellationToken = default)
    {
        BrowseFilter = filter ?? "";
        BrowsePage = 0;
        return SelectedCollection is null
            ? Task.CompletedTask
            : LoadCollectionAsync(SelectedCollection, cancellationToken);
    }

    public Task BrowsePreviousAsync(CancellationToken cancellationToken = default)
    {
        if (!HasPreviousPage || SelectedCollection is null)
        {
            return Task.CompletedTask;
        }

        BrowsePage--;
        return LoadCollectionAsync(SelectedCollection, cancellationToken);
    }

    public Task BrowseNextAsync(CancellationToken cancellationToken = default)
    {
        if (!HasNextPage || SelectedCollection is null)
        {
            return Task.CompletedTask;
        }

        BrowsePage++;
        return LoadCollectionAsync(SelectedCollection, cancellationToken);
    }

    public string ResolveSample(ExplorerQuerySample sample) => sample.Resolve(SelectedCollection);

    public async Task QueryAsync(string text, CancellationToken cancellationToken = default)
    {
        var docs = await Session.QueryAsync(text, cancellationToken).ConfigureAwait(false);
        QueryRows = Session.ToGrid(docs);
        SelectedQueryRow = QueryRows.Count == 0 ? null : QueryRows[0];
        QueryStatus = $"{docs.Count} document(s).";
        Status = QueryStatus;
        var plan = await Session.ExplainQueryAsync(text, cancellationToken).ConfigureAwait(false);
        QueryExplain = ExplorerSession.FormatExplain(plan, docs.Count);
        RefreshCaptions();
    }

    public void SelectRow(DocumentRow? row) => SelectedRow = row;

    public void SelectQueryRow(DocumentRow? row) => SelectedQueryRow = row;

    public Task ImportJsonAsync(string collection, string jsonArray, CancellationToken cancellationToken = default) =>
        Session.ImportJsonAsync(collection, jsonArray, cancellationToken);

    public string ExportJson() => ExplorerSession.ExportJson(
        Rows.Select(r => NuvexaDocument.Parse(r.Json)));

    public string ExportQueryJson() => ExplorerSession.ExportJson(
        QueryRows.Select(r => NuvexaDocument.Parse(r.Json)));

    public Task CompactAsync(CancellationToken cancellationToken = default) => Session.CompactAsync(cancellationToken);

    public Task ChangeEncryptionKeyAsync(string currentKey, string nextKey, CancellationToken cancellationToken = default) =>
        Session.ChangeEncryptionKeyAsync(currentKey, nextKey, cancellationToken);

    public ValueTask DisposeAsync() => Session.DisposeAsync();

    private void ApplyBrowsePage(BrowsePageResult page)
    {
        BrowsePage = page.Page;
        BrowsePageText = page.PageText;
        BrowseStatus = page.Status;
        HasPreviousPage = page.HasPrevious;
        HasNextPage = page.HasNext;
        Explain = page.Explain;
        Rows = Session.ToGrid(page.Documents);
        SelectedRow = Rows.Count == 0 ? null : Rows[0];
        Status = SelectedCollection is null ? page.Status : $"{SelectedCollection}: {page.Status}";
    }

    private void RefreshCaptions()
    {
        if (!Session.IsOpen)
        {
            return;
        }

        foreach (var node in Tree)
        {
            if (node.Kind == "collection")
            {
                node.Detail = Session.CollectionCount(node.Name).ToString();
            }
        }
    }

    private string? FindCollectionName(ExplorerNode node)
    {
        foreach (var collection in Tree)
        {
            if (Contains(collection, node))
            {
                return collection.Name;
            }
        }

        return null;
    }

    private static bool Contains(ExplorerNode root, ExplorerNode target)
    {
        if (ReferenceEquals(root, target))
        {
            return true;
        }

        foreach (var child in root.Children)
        {
            if (Contains(child, target))
            {
                return true;
            }
        }

        return false;
    }
}
