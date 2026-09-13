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
    public IReadOnlyList<DocumentRow> PageRows { get; private set; } = [];
    public IReadOnlyList<DocumentRow> Rows { get; private set; } = [];
    public IReadOnlyList<DocumentRow> QueryRows { get; private set; } = [];
    public IReadOnlyList<ExplorerQuerySample> QuerySamples { get; } = ExplorerQuerySample.All;
    public string Status { get; private set; } = "Closed.";
    public string AboutText => NuvexaAbout.PlainText("Visual Studio");
    public string Explain { get; private set; } = "";
    public string QueryExplain { get; private set; } = "";
    public string QueryStatus { get; private set; } = "";
    public string BrowseFilter { get; set; } = "";
    public string GridFindText { get; set; } = "";
    public string BrowseFindStatus { get; private set; } = "";
    public IReadOnlyList<string> BrowseFields { get; private set; } = ["_id"];
    public IReadOnlyList<JsonDocumentNode> DocumentTree { get; private set; } = [];
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
    private string? _browseSortField;
    private bool _browseSortDescending;

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

    public Task ApplyBuiltFilterAsync(string? field, string? op, string? value, CancellationToken cancellationToken = default)
    {
        BrowseFilter = BrowseFilterBuilder.Build(field, op, value);
        BrowsePage = 0;
        return SelectedCollection is null
            ? Task.CompletedTask
            : LoadCollectionAsync(SelectedCollection, cancellationToken);
    }

    public void ApplyGridFind(string? text)
    {
        GridFindText = text ?? "";
        ShowDisplayedRows(SelectedRow?.Id);
    }

    public void SortBrowsePage(string field)
    {
        if (string.Equals(_browseSortField, field, StringComparison.Ordinal))
        {
            _browseSortDescending = !_browseSortDescending;
        }
        else
        {
            _browseSortField = field;
            _browseSortDescending = false;
        }

        ShowDisplayedRows(SelectedRow?.Id);
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

    public void SelectRow(DocumentRow? row)
    {
        SelectedRow = row;
        DocumentTree = JsonDocumentTree.Parse(DocumentJson);
    }

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
        PageRows = Session.ToGrid(page.Documents);
        BrowseFields = MergeFieldNames(PageRows);
        ShowDisplayedRows(SelectedRow?.Id);
        Status = SelectedCollection is null ? page.Status : $"{SelectedCollection}: {page.Status}";
    }

    private void ShowDisplayedRows(string? keepId)
    {
        IEnumerable<DocumentRow> ordered = _browseSortField is "_id"
            ? PageRows.OrderBy(r => r.Id, StringComparer.Ordinal)
            : string.IsNullOrEmpty(_browseSortField)
                ? PageRows
                : PageRows.OrderBy(r => r.Cells[_browseSortField], StringComparer.Ordinal);
        if (_browseSortDescending && !string.IsNullOrEmpty(_browseSortField))
        {
            ordered = ordered.Reverse();
        }

        Rows = BrowseFilterBuilder.FindInPage(ordered, GridFindText);
        SelectedRow = Rows.FirstOrDefault(r => r.Id == keepId) ?? Rows.FirstOrDefault();
        DocumentTree = JsonDocumentTree.Parse(DocumentJson);
        BrowseFindStatus = string.IsNullOrWhiteSpace(GridFindText)
            ? ""
            : $"Find: {Rows.Count} of {PageRows.Count} on this page.";
    }

    private static IReadOnlyList<string> MergeFieldNames(IReadOnlyList<DocumentRow> rows)
    {
        var names = new List<string> { "_id" };
        foreach (var row in rows)
        {
            foreach (var key in row.Cells.Keys)
            {
                if (!names.Contains(key, StringComparer.Ordinal))
                {
                    names.Add(key);
                }
            }
        }

        return names;
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
