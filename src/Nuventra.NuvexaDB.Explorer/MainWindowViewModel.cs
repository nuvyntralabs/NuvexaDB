using System.Collections.ObjectModel;
using Plugin.Avalonia.MVVMExpress.ComponentModel;
using Plugin.Avalonia.MVVMExpress.Dialogs;
using Plugin.Avalonia.MVVMExpress.Input;
using Plugin.Avalonia.MVVMExpress.Threading;
using Nuventra.NuvexaDB.Documents;
using Nuventra.NuvexaDB.Query;
using Nuventra.NuvexaDB.Tools;

namespace Nuventra.NuvexaDB.Explorer;

public sealed partial class MainWindowViewModel : PageViewModel
{
    private readonly IExplorerShell _shell;
    private readonly ExplorerSession _session;
    private string _status = "Create or open a .nvx database to begin.";
    private string _queryText = "db.users.find({}).limit(50)";
    private string _documentJson = "";
    private string _explainText = "";
    private string _queryErrorText = "";
    private string _browseFilterText = "";
    private string _browseFilterError = "";
    private string _browseStatusText = "";
    private string _structureDetail = "A collection is like a SQL table. A document is one JSON row in that table.";
    private string _structureHeading = "";
    private string _structureSummary = "";
    private bool _showStructureEditor;
    private TableColumnDefinition? _selectedStructureColumn;
    private string? _pendingStructureField;
    private int _structureLoadVersion;
    private string? _selectedCollection;
    private ExplorerNode? _selectedNode;
    private DocumentRow? _selectedRow;
    private int _selectedTabIndex;
    private bool _suspendCollectionLoad;
    private bool _clearingCollection;
    private bool _applyingHistory;
    private int _browseLoadVersion;
    private IReadOnlyList<TableColumnDefinition> _browseColumns = [];
    private string? _loadedBrowseCollection;
    private DocumentRow? _selectedQueryRow;
    private ExplorerQuerySample? _selectedQuerySample;
    private string? _selectedHistoryItem;
    private string _treeMenuKind = "none";
    private int _browsePage;
    private string _browsePageText = "";
    private bool _canBrowsePrevious;
    private bool _canBrowseNext;
    internal const int BrowsePageSize = 200;

    public MainWindowViewModel(
        IExplorerShell shell,
        ExplorerSession session,
        IDialogs dialogs,
        IMainThread mainThread)
        : base(dialogs: dialogs, mainThread: mainThread)
    {
        _shell = shell;
        _session = session;
        NewCommand = new AsyncModelCommand(_ => NewAsync());
        OpenCommand = new AsyncModelCommand(_ => OpenAsync());
        CloseCommand = new AsyncModelCommand(_ => CloseAsync(), () => _session.IsOpen);
        RunQueryCommand = new AsyncModelCommand(_ => RunQueryAsync(), () => _session.IsOpen);
        ImportCommand = new AsyncModelCommand(_ => ImportAsync(), () => CanMutate && SelectedCollection is not null);
        ExportCommand = new AsyncModelCommand(_ => ExportAsync(), () => _session.IsOpen && SelectedCollection is not null);
        ExportQueryCommand = new AsyncModelCommand(_ => ExportQueryAsync(), () => _session.IsOpen && QueryRows.Count > 0);
        ApplyBrowseFilterCommand = new AsyncModelCommand(_ => ApplyBrowseFilterAsync(), () => _session.IsOpen && SelectedCollection is not null);
        BrowsePreviousCommand = new AsyncModelCommand(_ => MoveBrowsePageAsync(-1), () => CanBrowsePrevious);
        BrowseNextCommand = new AsyncModelCommand(_ => MoveBrowsePageAsync(1), () => CanBrowseNext);
        CreateIndexCommand = new AsyncModelCommand(_ => CreateIndexAsync(), () => CanMutate && SelectedCollection is not null);
        DropIndexCommand = new AsyncModelCommand(_ => DropIndexAsync(), () => CanDropIndex);
        NewCollectionCommand = new AsyncModelCommand(_ => NewCollectionAsync(), () => CanMutate);
        EditTableDefinitionCommand = new AsyncModelCommand(_ => EditTableDefinitionAsync(), () => CanMutate && SelectedCollection is not null);
        BuildAggregateCommand = new AsyncModelCommand(_ => BuildAggregateAsync(), () => _session.IsOpen);
        VisualExplainCommand = new AsyncModelCommand(_ => VisualExplainAsync(), () => _session.IsOpen);
        NewColumnCommand = new AsyncModelCommand(_ => NewColumnAsync(), () => CanMutate && SelectedCollection is not null);
        EditColumnCommand = new AsyncModelCommand(_ => EditColumnAsync(), () => CanEditColumn);
        DeleteColumnCommand = new AsyncModelCommand(_ => DeleteColumnAsync(), () => CanMutate && SelectedCollection is not null);
        NewDocumentCommand = new AsyncModelCommand(_ => NewDocumentAsync(), () => CanMutate && SelectedCollection is not null);
        DeleteDocumentCommand = new AsyncModelCommand(_ => DeleteDocumentAsync(), () => CanMutate && SelectedCollection is not null && HasBrowseSelection);
        ChangeKeyCommand = new AsyncModelCommand(_ => ChangeKeyAsync(), () => CanMutate);
        CompactCommand = new AsyncModelCommand(_ => CompactAsync(), () => CanMutate);
        RefreshTreeCommand = new AsyncModelCommand(_ => RefreshAsync());
        BrowseCollectionCommand = new AsyncModelCommand(_ => BrowseSelectedAsync(), () => _session.IsOpen && SelectedCollection is not null);
        GoToStructureCommand = new ModelCommand(() => SelectedTabIndex = 0);
        GoToBrowseCommand = new AsyncModelCommand(_ => BrowseSelectedAsync(), () => _session.IsOpen && SelectedCollection is not null);
        GoToQueryCommand = new ModelCommand(() => SelectedTabIndex = 2, () => _session.IsOpen);
        DeleteCollectionCommand = new AsyncModelCommand(_ => DeleteCollectionAsync(), () => CanMutate && SelectedCollection is not null);
        RenameCollectionCommand = new AsyncModelCommand(_ => RenameCollectionAsync(), () => CanMutate && SelectedCollection is not null);
        ClearRecentCommand = new ModelCommand(ClearRecentFiles);
        EditRecordCommand = new AsyncModelCommand(_ => EditRecordAsync(), () => CanMutate && SelectedCollection is not null && SelectedRow is not null);
        CopyCellCommand = new AsyncModelCommand(_ => CopyCellAsync(), () => SelectedRow is not null);
        CopyRowCommand = new AsyncModelCommand(_ => CopyRowAsync(), () => SelectedRow is not null);
        CopyQueryRowCommand = new AsyncModelCommand(_ => CopyQueryRowAsync(), () => SelectedQueryRow is not null);
        ExitCommand = new ModelCommand(() => _shell.Exit());
        foreach (var item in QueryHistoryStore.Load())
        {
            QueryHistory.Add(item);
        }

        foreach (var item in RecentFilesStore.Load())
        {
            RecentFiles.Add(item);
        }

        InitializeWorkbench();
    }

    public ObservableCollection<ExplorerNode> Tree { get; } = [];
    public ObservableCollection<TableColumnDefinition> StructureColumns { get; } = [];
    public ObservableCollection<string> CollectionNames { get; } = [];
    public ObservableCollection<DocumentRow> GridRows { get; } = [];
    public ObservableCollection<DocumentRow> QueryRows { get; } = [];
    public ObservableCollection<string> QueryHistory { get; } = [];
    public ObservableCollection<string> RecentFiles { get; } = [];
    public event Action? RecentFilesChanged;
    public event Action<ExplorerNode>? TreeSelectionRequested;
    public IReadOnlyList<ExplorerQuerySample> QuerySamples { get; } = ExplorerQuerySample.All;
    public event Action? GridAboutToReset;
    public event Action<IReadOnlyList<string>>? GridSchemaChanged;
    public event Action<IReadOnlyList<string>>? QuerySchemaChanged;

    public AsyncModelCommand NewCommand { get; }
    public AsyncModelCommand OpenCommand { get; }
    public AsyncModelCommand CloseCommand { get; }
    public AsyncModelCommand RunQueryCommand { get; }
    public AsyncModelCommand ImportCommand { get; }
    public AsyncModelCommand ExportCommand { get; }
    public AsyncModelCommand ExportQueryCommand { get; }
    public AsyncModelCommand ApplyBrowseFilterCommand { get; }
    public AsyncModelCommand BrowsePreviousCommand { get; }
    public AsyncModelCommand BrowseNextCommand { get; }
    public AsyncModelCommand CreateIndexCommand { get; }
    public AsyncModelCommand DropIndexCommand { get; }
    public AsyncModelCommand NewCollectionCommand { get; }
    public AsyncModelCommand EditTableDefinitionCommand { get; }
    public AsyncModelCommand BuildAggregateCommand { get; }
    public AsyncModelCommand VisualExplainCommand { get; }
    public AsyncModelCommand NewColumnCommand { get; }
    public AsyncModelCommand EditColumnCommand { get; }
    public AsyncModelCommand DeleteColumnCommand { get; }
    public AsyncModelCommand NewDocumentCommand { get; }
    public AsyncModelCommand DeleteDocumentCommand { get; }
    public AsyncModelCommand ChangeKeyCommand { get; }
    public AsyncModelCommand CompactCommand { get; }
    public AsyncModelCommand RefreshTreeCommand { get; }
    public AsyncModelCommand BrowseCollectionCommand { get; }
    public ModelCommand GoToStructureCommand { get; }
    public AsyncModelCommand GoToBrowseCommand { get; }
    public ModelCommand GoToQueryCommand { get; }
    public AsyncModelCommand DeleteCollectionCommand { get; }
    public AsyncModelCommand RenameCollectionCommand { get; }
    public ModelCommand ClearRecentCommand { get; }
    public AsyncModelCommand EditRecordCommand { get; }
    public AsyncModelCommand CopyCellCommand { get; }
    public AsyncModelCommand CopyRowCommand { get; }
    public AsyncModelCommand CopyQueryRowCommand { get; }
    public ModelCommand ExitCommand { get; }

    public string? ContextField { get; set; }

    public bool ShowCollectionMenu => _treeMenuKind == "collection";
    public bool ShowColumnMenu => _treeMenuKind == "field";
    public bool ShowIndexMenu => _treeMenuKind == "index";
    public bool ShowEmptyTreeMenu => _treeMenuKind == "empty";
    public bool ShowDeleteColumnMenu => ShowCollectionMenu || ShowColumnMenu;
    public bool HasTreeContextMenu => ShowCollectionMenu || ShowColumnMenu || ShowIndexMenu || ShowEmptyTreeMenu;
    public bool HasQueryError => !string.IsNullOrEmpty(QueryErrorText);
    public bool HasBrowseFilterError => !string.IsNullOrEmpty(BrowseFilterError);
    public bool ShowBrowsePager => CanBrowsePrevious || CanBrowseNext;
    public bool HasRecentFiles => RecentFiles.Count > 0;
    public IReadOnlyDictionary<string, string> BrowseColumnTypes { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    private bool HasBrowseSelection => SelectedRow is not null || _selectedBrowseRows.Count > 0;

    private bool CanEditColumn =>
        CanMutate && SelectedCollection is not null &&
        ActiveColumnName is not null && ActiveColumnName != "_id";

    private string? ActiveColumnName =>
        SelectedStructureColumn is { Name: { Length: > 0 } name } ? name
        : SelectedNode?.Kind == "field" ? SelectedNode.Name
        : null;
    private bool CanDropIndex =>
        CanMutate && SelectedCollection is not null && SelectedNode?.Kind == "index" && SelectedNode.Name != "_id_";

    public string StatusText
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string QueryText
    {
        get => _queryText;
        set => SetProperty(ref _queryText, value);
    }

    public string DocumentJson
    {
        get => _documentJson;
        set
        {
            if (SetProperty(ref _documentJson, value))
            {
                RebuildDocumentTree();
            }
        }
    }

    public string ExplainText
    {
        get => _explainText;
        set => SetProperty(ref _explainText, value);
    }

    public string QueryErrorText
    {
        get => _queryErrorText;
        set
        {
            if (SetProperty(ref _queryErrorText, value))
            {
                Notify(nameof(HasQueryError));
            }
        }
    }

    public string BrowseFilterText
    {
        get => _browseFilterText;
        set => SetProperty(ref _browseFilterText, value);
    }

    public string BrowseFilterError
    {
        get => _browseFilterError;
        set
        {
            if (SetProperty(ref _browseFilterError, value))
            {
                Notify(nameof(HasBrowseFilterError));
            }
        }
    }

    public string BrowseStatusText
    {
        get => _browseStatusText;
        set => SetProperty(ref _browseStatusText, value);
    }

    public string BrowsePageText
    {
        get => _browsePageText;
        set => SetProperty(ref _browsePageText, value);
    }

    public bool CanBrowsePrevious
    {
        get => _canBrowsePrevious;
        private set
        {
            if (SetProperty(ref _canBrowsePrevious, value))
            {
                BrowsePreviousCommand.NotifyCanExecuteChanged();
                Notify(nameof(ShowBrowsePager));
            }
        }
    }

    public bool CanBrowseNext
    {
        get => _canBrowseNext;
        private set
        {
            if (SetProperty(ref _canBrowseNext, value))
            {
                BrowseNextCommand.NotifyCanExecuteChanged();
                Notify(nameof(ShowBrowsePager));
            }
        }
    }

    public ExplorerQuerySample? SelectedQuerySample
    {
        get => _selectedQuerySample;
        set
        {
            if (SetProperty(ref _selectedQuerySample, value) && value is not null)
            {
                QueryText = value.Resolve(SelectedCollection);
            }
        }
    }

    public string? SelectedHistoryItem
    {
        get => _selectedHistoryItem;
        set
        {
            if (!SetProperty(ref _selectedHistoryItem, value) || string.IsNullOrEmpty(value) || _applyingHistory)
            {
                return;
            }

            QueryText = value;
        }
    }

    public string StructureDetail
    {
        get => _structureDetail;
        set => SetProperty(ref _structureDetail, value);
    }

    public string StructureHeading
    {
        get => _structureHeading;
        private set => SetProperty(ref _structureHeading, value);
    }

    public string StructureSummary
    {
        get => _structureSummary;
        private set => SetProperty(ref _structureSummary, value);
    }

    public bool ShowStructureEditor
    {
        get => _showStructureEditor;
        private set
        {
            if (SetProperty(ref _showStructureEditor, value))
            {
                Notify(nameof(ShowStructureHint));
            }
        }
    }

    public bool ShowStructureHint => !ShowStructureEditor;

    public TableColumnDefinition? SelectedStructureColumn
    {
        get => _selectedStructureColumn;
        set
        {
            if (SetProperty(ref _selectedStructureColumn, value))
            {
                NotifyCrudCommands();
            }
        }
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (!SetProperty(ref _selectedTabIndex, value))
            {
                return;
            }

            if (value == 1)
            {
                _ = EnsureBrowseVisibleAsync();
            }
        }
    }

    public bool IsDatabaseOpen => _session.IsOpen;

    public bool HasCollections => Tree.Count > 0;

    public string NavigatorHint => !_session.IsOpen
        ? "Open or create a database. Each collection is a table; each document is a row."
        : Tree.Count == 0
            ? "No collections yet. Right-click the empty list to add a collection."
            : "Right-click a collection to browse, rename, add a column, or create an index. Right-click a column or index to change it.";

    public string? SelectedCollection
    {
        get => _selectedCollection;
        set
        {
            if (value is null && _session.IsOpen && !_clearingCollection)
            {
                Notify(nameof(SelectedCollection));
                Notify(nameof(SelectedCollectionIndex));
                return;
            }

            if (!SetProperty(ref _selectedCollection, value))
            {
                Notify(nameof(SelectedCollectionIndex));
                return;
            }

            if (!string.IsNullOrEmpty(value))
            {
                QueryText = $"db.{value}.find({{}}).limit(200)";
                if (!string.Equals(_loadedBrowseCollection, value, StringComparison.Ordinal))
                {
                    BrowseFilterText = "";
                    BrowseFilterError = "";
                    _browsePage = 0;
                }
            }

            NotifyCrudCommands();
            Notify(nameof(SelectedCollectionIndex));
            if (!_suspendCollectionLoad && value is not null && _session.IsOpen && SelectedTabIndex == 1)
            {
                _ = LoadCollectionAsync(value);
            }
        }
    }

    public int SelectedCollectionIndex
    {
        get => SelectedCollection is null ? -1 : CollectionNames.IndexOf(SelectedCollection);
        set
        {
            if (value >= 0 && value < CollectionNames.Count)
            {
                SelectedCollection = CollectionNames[value];
            }
        }
    }

    public ExplorerNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (!SetProperty(ref _selectedNode, value) || value is null)
            {
                return;
            }

            var collection = value.Kind == "collection" ? value.Name : FindCollectionName(value);
            if (collection is not null)
            {
                SelectCollection(collection, loadBrowse: SelectedTabIndex == 1);
            }

            StructureDetail = DescribeNode(value, collection);
            StatusText = StructureDetail.Split('\n')[0];
            _pendingStructureField = value.Kind == "field" ? value.Name : null;
            _ = LoadStructureAsync(collection);
        }
    }

    public DocumentRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value))
            {
                DocumentJson = value?.Json ?? "";
                NotifyCrudCommands();
            }
        }
    }

    public DocumentRow? SelectedQueryRow
    {
        get => _selectedQueryRow;
        set
        {
            if (SetProperty(ref _selectedQueryRow, value))
            {
                NotifyCrudCommands();
            }
        }
    }

    public async Task OpenPathAsync(string path)
    {
        try
        {
            string? key = null;
            if (ExplorerSession.PeekEncrypted(path))
            {
                key = await _shell.PromptKeyAsync("This database is encrypted. Enter the encryption key:");
                if (string.IsNullOrEmpty(key))
                {
                    StatusText = "Open cancelled.";
                    return;
                }
            }

            await _session.OpenAsync(path, key);
            RememberRecent(path);
            await RefreshAsync();
            SelectedTabIndex = 0;
            NotifyCrudCommands();
        }
        catch (NuvexaEncryptionException)
        {
            StatusText = "Wrong encryption key. The file was not opened.";
            if (Dialogs is not null)
            {
                await Dialogs.AlertAsync("Nuvexa Data Studio", "Wrong encryption key. The file was not opened.");
            }
        }
        catch (NuvexaIntegrityException ex)
        {
            StatusText = ex.Message;
            if (Dialogs is not null)
            {
                await Dialogs.AlertAsync(
                    "Nuvexa Data Studio",
                    "This database file is corrupt or has been tampered with. It was not opened.");
            }
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            if (Dialogs is not null)
            {
                await Dialogs.AlertAsync("Nuvexa Data Studio", ex.Message);
            }
        }
    }

    private async Task NewAsync()
    {
        var path = await _shell.PickSaveNvxAsync();
        if (path is null)
        {
            return;
        }

        var key = await _shell.PromptKeyAsync("Optional encryption key (leave empty for none):");
        await _session.CreateAsync(path, string.IsNullOrWhiteSpace(key) ? null : key);
        RememberRecent(path);
        await RefreshAsync();
        SelectedTabIndex = 0;
        NotifyCrudCommands();
    }

    private async Task OpenAsync()
    {
        var path = await _shell.PickOpenNvxAsync();
        if (path is null)
        {
            return;
        }

        await OpenPathAsync(path);
    }

    private async Task CloseAsync()
    {
        var closedPath = _session.Path;
        await _session.DisposeAsync();
        Tree.Clear();
        CollectionNames.Clear();
        GridRows.Clear();
        QueryRows.Clear();
        ClearWorkbenchState();
        DocumentJson = "";
        QueryErrorText = "";
        ExplainText = "";
        BrowseFilterText = "";
        BrowseFilterError = "";
        BrowseStatusText = "";
        BrowsePageText = "";
        _browsePage = 0;
        CanBrowsePrevious = false;
        CanBrowseNext = false;
        StructureDetail = "A collection is like a SQL table. A document is one JSON row in that table.";
        ClearStructureEditor();
        _loadedBrowseCollection = null;
        ClearCollectionSelection();
        SelectedRow = null;
        SelectedQueryRow = null;
        StatusText = ClosedDatabaseHint(closedPath) ?? "Database closed.";
        NotifyCrudCommands();
    }

    private async Task RunQueryAsync()
    {
        if (!_session.IsOpen)
        {
            return;
        }

        try
        {
            if (NuvexaWriteQuery.TryParse(QueryText, out var write) && write.IsDelete && Dialogs is not null)
            {
                var ok = await Dialogs.ConfirmAsync(
                    "Delete documents",
                    $"Run this delete on '{write.Collection}'? Matching documents are removed.",
                    "Delete",
                    "Cancel");
                if (!ok)
                {
                    return;
                }
            }

            var result = await _session.ExecuteAsync(QueryText);
            var docs = result.Documents;
            var rows = _session.ToGrid(docs);
            var fields = rows.Count == 0 ? new List<string>() : rows[0].Cells.Keys.ToList();
            QueryRows.Clear();
            QuerySchemaChanged?.Invoke(fields);
            foreach (var row in rows)
            {
                QueryRows.Add(row);
            }
            RememberQuery(QueryText);
            QueryErrorText = "";
            SelectedTabIndex = 2;
            if (result.Operation is "update" or "delete")
            {
                StatusText = $"{result.Operation} affected {result.Affected} document(s).";
                ExplainText = $"{result.Operation.ToUpperInvariant()}  collection={result.Collection}  affected={result.Affected}";
                await RefreshAsync();
            }
            else
            {
                StatusText = $"{docs.Count} row(s) returned.";
                try
                {
                    var plan = await _session.ExplainQueryAsync(QueryText);
                    ExplainText = ExplorerSession.FormatExplain(plan, docs.Count);
                }
                catch
                {
                    ExplainText = $"{docs.Count} row(s) returned.";
                }
            }
        }
        catch (Exception ex)
        {
            QueryErrorText = ex.Message;
            StatusText = ex.Message;
        }
        finally
        {
            ExportQueryCommand.NotifyCanExecuteChanged();
            ExportQueryCsvCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task ImportAsync()
    {
        if (SelectedCollection is null || !_session.IsOpen)
        {
            return;
        }

        var path = await _shell.PickOpenJsonAsync();
        if (path is null)
        {
            return;
        }

        var json = await File.ReadAllTextAsync(path);
        await _session.ImportJsonAsync(SelectedCollection, json);
        await RefreshAsync();
        SelectedTabIndex = 1;
    }

    private async Task ExportAsync()
    {
        if (SelectedCollection is null || !_session.IsOpen)
        {
            return;
        }

        var docs = await _session.ListAsync(SelectedCollection, 10_000);
        var path = await _shell.PickSaveJsonAsync();
        if (path is null)
        {
            return;
        }

        await File.WriteAllTextAsync(path, ExplorerSession.ExportJson(docs));
        StatusText = $"Exported collection JSON to {Path.GetFileName(path)}.";
    }

    private async Task ExportQueryAsync()
    {
        if (QueryRows.Count == 0)
        {
            StatusText = "No query results to export.";
            return;
        }

        var path = await _shell.PickSaveJsonAsync();
        if (path is null)
        {
            return;
        }

        var json = "[" + string.Join(",", QueryRows.Select(r => r.Json)) + "]";
        await File.WriteAllTextAsync(path, json);
        StatusText = $"Exported {QueryRows.Count} query row(s) to JSON file {Path.GetFileName(path)}.";
    }

    private async Task ChangeKeyAsync()
    {
        if (!_session.IsOpen)
        {
            StatusText = "Open a database first.";
            return;
        }

        if (!_session.Encrypted)
        {
            StatusText = "This database is not encrypted.";
            return;
        }

        var keys = await _shell.PromptChangeKeyAsync();
        if (keys is null)
        {
            return;
        }

        try
        {
            await _session.ChangeEncryptionKeyAsync(keys.Value.Current, keys.Value.Next);
            StatusText = "Encryption key updated. Use the new key the next time you open this file.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    private async Task CompactAsync()
    {
        if (!_session.IsOpen)
        {
            return;
        }

        try
        {
            await _session.CompactAsync();
            await RefreshAsync();
            StatusText = "Compacted.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    private async Task EditTableDefinitionAsync()
    {
        if (!_session.IsOpen || SelectedCollection is null)
        {
            return;
        }

        var columns = await _session.GetDeclaredColumnsAsync(SelectedCollection);
        TableDefinition? definition;
        try
        {
            definition = await _shell.PromptTableDefinitionAsync(new TableDefinition(SelectedCollection, columns.ToList()));
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            return;
        }

        if (definition is null)
        {
            return;
        }

        try
        {
            await _session.ApplyTableDefinitionAsync(SelectedCollection, definition);
            await RefreshAsync();
            StatusText = $"Updated table definition for '{SelectedCollection}'.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            if (Dialogs is not null)
            {
                await Dialogs.AlertAsync("Nuvexa Data Studio", ex.Message);
            }
        }
    }

    private async Task BuildAggregateAsync()
    {
        var built = await _shell.PromptAggregateAsync(SelectedCollection ?? "users", QueryText);
        if (!string.IsNullOrWhiteSpace(built))
        {
            QueryText = built;
            SelectedTabIndex = 2;
        }
    }

    private async Task VisualExplainAsync()
    {
        if (!_session.IsOpen || string.IsNullOrWhiteSpace(QueryText))
        {
            return;
        }

        try
        {
            var plan = await _session.ExplainQueryAsync(QueryText);
            await _shell.ShowExplainAsync(
                $"{plan.Strategy} — {plan.Collection}",
                ExplorerSession.FormatExplain(plan) +
                $"\n\nStrategy: {plan.Strategy}\nIndex: {plan.IndexName ?? "none"}\nExamined: {plan.Examined}\nReturned: {plan.Returned}");
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    private async Task NewCollectionAsync()
    {
        if (!_session.IsOpen)
        {
            return;
        }

        TableDefinition? definition;
        try
        {
            definition = await _shell.PromptTableDefinitionAsync();
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            if (Dialogs is not null)
            {
                await Dialogs.AlertAsync("Nuvexa Data Studio", ex.Message);
            }

            return;
        }

        if (definition is null)
        {
            return;
        }

        try
        {
            await _session.CreateCollectionAsync(definition);
            await RefreshAsync();
            SelectCollection(definition.Name, loadBrowse: false);
            SelectedTabIndex = 0;
            var columns = definition.Columns.Count;
            StatusText = columns == 0
                ? $"Collection '{definition.Name}' created. Right-click it to add columns or browse data."
                : $"Collection '{definition.Name}' created with {columns} column{(columns == 1 ? "" : "s")}. Double-click to browse rows.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    private async Task NewColumnAsync()
    {
        if (SelectedCollection is null || !_session.IsOpen)
        {
            StatusText = "Select a collection first.";
            return;
        }

        var column = await _shell.PromptColumnAsync();
        if (column is null)
        {
            return;
        }

        try
        {
            await _session.AddColumnAsync(SelectedCollection, column);
            var stayOnBrowse = SelectedTabIndex == 1;
            await RefreshAsync();
            if (stayOnBrowse)
            {
                SelectedTabIndex = 1;
            }

            StatusText = $"Column '{column.Name}' added to '{SelectedCollection}'.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            if (Dialogs is not null)
            {
                await Dialogs.AlertAsync("Nuvexa Data Studio", ex.Message);
            }
        }
    }

    private async Task EditColumnAsync()
    {
        if (SelectedCollection is null || !_session.IsOpen)
        {
            StatusText = "Select a collection first.";
            return;
        }

        var oldName = ActiveColumnName;
        if (string.IsNullOrEmpty(oldName) || oldName == "_id")
        {
            StatusText = "Select a column to edit.";
            return;
        }
        var existing = (await ColumnsForEditorAsync(SelectedCollection))
            .FirstOrDefault(c => string.Equals(c.Name, oldName, StringComparison.Ordinal))
            ?? new TableColumnDefinition(oldName, "TEXT", "", Unique: false);
        var column = await _shell.PromptColumnAsync(existing);
        if (column is null)
        {
            return;
        }

        try
        {
            await _session.UpdateColumnAsync(SelectedCollection, oldName, column);
            await RefreshStructureAfterColumnChangeAsync();
            StatusText = string.Equals(oldName, column.Name, StringComparison.Ordinal)
                ? $"Updated column '{column.Name}'."
                : $"Renamed column '{oldName}' to '{column.Name}'.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            if (Dialogs is not null)
            {
                await Dialogs.AlertAsync("Nuvexa Data Studio", ex.Message);
            }
        }
    }

    private async Task DeleteColumnAsync()
    {
        if (SelectedCollection is null || !_session.IsOpen)
        {
            StatusText = "Select a collection first.";
            return;
        }

        var field = ActiveColumnName;
        if (string.IsNullOrEmpty(field))
        {
            var columns = await ColumnsForEditorAsync(SelectedCollection);
            if (columns.Count == 0)
            {
                StatusText = "This collection has no columns to delete.";
                return;
            }

            if (columns.Count == 1)
            {
                field = columns[0].Name;
            }
            else
            {
                field = await _shell.PromptTextAsync(
                    $"Column to delete ({string.Join(", ", columns.Select(c => c.Name))}):");
            }
        }

        if (string.IsNullOrWhiteSpace(field) || field == "_id")
        {
            return;
        }

        if (Dialogs is not null &&
            !await Dialogs.ConfirmAsync("Delete Column", $"Delete column '{field}' from '{SelectedCollection}'?", "Delete", "Cancel"))
        {
            return;
        }

        try
        {
            await _session.DropColumnAsync(SelectedCollection, field);
            await RefreshStructureAfterColumnChangeAsync();
            StatusText = $"Deleted column '{field}'.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            if (Dialogs is not null)
            {
                await Dialogs.AlertAsync("Nuvexa Data Studio", ex.Message);
            }
        }
    }

    private async Task RefreshStructureAfterColumnChangeAsync()
    {
        var stayOnBrowse = SelectedTabIndex == 1;
        await RefreshAsync();
        if (stayOnBrowse)
        {
            SelectedTabIndex = 1;
        }
    }

    public void SetTreeContext(ExplorerNode? node)
    {
        var next = node is null
            ? (_session.IsOpen ? "empty" : "none")
            : node.Kind switch
            {
                "collection" => "collection",
                "field" => "field",
                "index" => "index",
                _ => "none"
            };

        if (node is not null)
        {
            SelectedNode = node;
        }

        if (_treeMenuKind == next)
        {
            NotifyCrudCommands();
            return;
        }

        _treeMenuKind = next;
        Notify(nameof(ShowCollectionMenu));
        Notify(nameof(ShowColumnMenu));
        Notify(nameof(ShowIndexMenu));
        Notify(nameof(ShowEmptyTreeMenu));
        Notify(nameof(ShowDeleteColumnMenu));
        Notify(nameof(HasTreeContextMenu));
        NotifyCrudCommands();
    }

    private async Task NewDocumentAsync()
    {
        if (SelectedCollection is null || !CanMutate)
        {
            StatusText = "Select a collection first.";
            return;
        }

        try
        {
            var columns = await ColumnsForEditorAsync(SelectedCollection);
            var values = await _shell.PromptRecordAsync(SelectedCollection, columns);
            if (values is null)
            {
                return;
            }

            var json = ExplorerSession.DocumentJsonFromCells(null, values, columns);
            var collection = SelectedCollection!;
            var id = await _session.InsertDocumentAsync(collection, json);
            RefreshCollectionCaptions();
            if (string.IsNullOrWhiteSpace(BrowseFilterText))
            {
                var total = _session.CollectionCount(collection);
                _browsePage = total <= 0 ? 0 : (int)((total - 1) / BrowsePageSize);
            }

            await BrowseCollectionAsync(collection, resetPage: false);
            SelectedRow = GridRows.FirstOrDefault(r => r.Id == id);
            StatusText = $"Inserted record {id} in '{collection}' ({GridRows.Count} shown).";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            if (Dialogs is not null)
            {
                await Dialogs.AlertAsync("Nuvexa Data Studio", ex.Message);
            }
        }
    }

    public async Task CommitGridCellAsync(DocumentRow row, string field)
    {
        if (SelectedCollection is null || !CanMutate || field is "_id" || string.IsNullOrWhiteSpace(field))
        {
            return;
        }

        try
        {
            var json = ExplorerSession.DocumentJsonFromCells(row.Id, row.Cells.Snapshot(), _browseColumns);
            await _session.ReplaceDocumentAsync(SelectedCollection, json, row.Id);
            row.SetJson(json);
            if (ReferenceEquals(SelectedRow, row))
            {
                DocumentJson = json;
            }

            StatusText = $"Updated '{field}' on {row.Id}.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            if (Dialogs is not null)
            {
                await Dialogs.AlertAsync("Nuvexa Data Studio", ex.Message);
            }

            await LoadCollectionAsync(SelectedCollection);
        }
    }

    private async Task DeleteDocumentAsync()
    {
        if (SelectedCollection is null || !CanMutate)
        {
            StatusText = "Select a record to delete.";
            return;
        }

        var ids = _selectedBrowseRows.Count > 0
            ? _selectedBrowseRows.Select(r => r.Id).Distinct(StringComparer.Ordinal).ToList()
            : SelectedRow is null ? [] : [SelectedRow.Id];
        if (ids.Count == 0)
        {
            StatusText = "Select a record to delete.";
            return;
        }

        var prompt = ids.Count == 1
            ? $"Delete record '{ids[0]}'?"
            : $"Delete {ids.Count} selected records?";
        if (Dialogs is not null &&
            !await Dialogs.ConfirmAsync("Delete Record", prompt, "Delete", "Cancel"))
        {
            return;
        }

        try
        {
            foreach (var id in ids)
            {
                await _session.DeleteDocumentAsync(SelectedCollection, id);
            }

            SelectedRow = null;
            DocumentJson = "";
            _selectedBrowseRows.Clear();
            RefreshCollectionCaptions();
            await LoadCollectionAsync(SelectedCollection);
            if (GridRows.Count == 0 && _browsePage > 0)
            {
                _browsePage--;
                await LoadCollectionAsync(SelectedCollection);
            }

            StatusText = ids.Count == 1 ? $"Deleted {ids[0]}." : $"Deleted {ids.Count} records.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    private void NotifyCrudCommands()
    {
        NewCollectionCommand.NotifyCanExecuteChanged();
        EditTableDefinitionCommand.NotifyCanExecuteChanged();
        BuildAggregateCommand.NotifyCanExecuteChanged();
        VisualExplainCommand.NotifyCanExecuteChanged();
        NewColumnCommand.NotifyCanExecuteChanged();
        EditColumnCommand.NotifyCanExecuteChanged();
        DeleteColumnCommand.NotifyCanExecuteChanged();
        NewDocumentCommand.NotifyCanExecuteChanged();
        DeleteDocumentCommand.NotifyCanExecuteChanged();
        ImportCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
        ExportQueryCommand.NotifyCanExecuteChanged();
        ApplyBrowseFilterCommand.NotifyCanExecuteChanged();
        BrowsePreviousCommand.NotifyCanExecuteChanged();
        BrowseNextCommand.NotifyCanExecuteChanged();
        CreateIndexCommand.NotifyCanExecuteChanged();
        DropIndexCommand.NotifyCanExecuteChanged();
        RunQueryCommand.NotifyCanExecuteChanged();
        ChangeKeyCommand.NotifyCanExecuteChanged();
        CompactCommand.NotifyCanExecuteChanged();
        CloseCommand.NotifyCanExecuteChanged();
        BrowseCollectionCommand.NotifyCanExecuteChanged();
        GoToBrowseCommand.NotifyCanExecuteChanged();
        GoToQueryCommand.NotifyCanExecuteChanged();
        DeleteCollectionCommand.NotifyCanExecuteChanged();
        RenameCollectionCommand.NotifyCanExecuteChanged();
        EditRecordCommand.NotifyCanExecuteChanged();
        CopyCellCommand.NotifyCanExecuteChanged();
        CopyRowCommand.NotifyCanExecuteChanged();
        CopyQueryRowCommand.NotifyCanExecuteChanged();
        NotifyWorkbenchCommands();
        Notify(nameof(IsDatabaseOpen));
        Notify(nameof(HasCollections));
        Notify(nameof(NavigatorHint));
        Notify(nameof(SelectedCollectionIndex));
    }

    private async Task RefreshAsync()
    {
        var keepCollection = SelectedCollection;
        _suspendCollectionLoad = true;
        Tree.Clear();
        CollectionNames.Clear();
        if (!_session.IsOpen)
        {
            GridRows.Clear();
            _suspendCollectionLoad = false;
            NotifyCrudCommands();
            return;
        }

        foreach (var node in await _session.LoadTreeAsync())
        {
            Tree.Add(node);
            CollectionNames.Add(node.Name);
        }

        var next = keepCollection is not null && CollectionNames.Contains(keepCollection)
            ? keepCollection
            : CollectionNames.FirstOrDefault();
        if (next is not null)
        {
            _selectedCollection = next;
            Notify(nameof(SelectedCollection));
            Notify(nameof(SelectedCollectionIndex));
            SelectCollectionInTree(next);
        }

        _suspendCollectionLoad = false;

        var stats = _session.Stats();
        StatusText = $"{stats.Path}   records={stats.DocumentCount}   encrypted={stats.Encrypted}   {stats.FileBytes} bytes";
        StructureDetail = $"{stats.Path}\nEncrypted: {stats.Encrypted}\nCollections: {stats.CollectionCount}\nDocuments: {stats.DocumentCount}\nSize: {stats.FileBytes} bytes\n\nA collection is a table. A document is a JSON row.";
        NotifyCrudCommands();
        await LoadStructureAsync(SelectedCollection);
        if (SelectedTabIndex == 1 && SelectedCollection is not null && CollectionNames.Contains(SelectedCollection))
        {
            await LoadCollectionAsync(SelectedCollection);
        }
        else
        {
            _loadedBrowseCollection = null;
        }
    }

    private async Task LoadCollectionAsync(string collection)
    {
        var version = ++_browseLoadVersion;
        try
        {
            _suspendCollectionLoad = true;
            try
            {
                _ = ExplorerSession.NormalizeBrowseFilter(BrowseFilterText);
            }
            catch (Exception ex)
            {
                BrowseFilterError = ex.Message;
                StatusText = ex.Message;
                return;
            }

            _browseColumns = await ColumnsForEditorAsync(collection);
            RefreshBrowseFilterFields();
            BrowseColumnTypes = _browseColumns.ToDictionary(
                c => c.Name,
                c => TableColumnTypes.Normalize(c.Type),
                StringComparer.Ordinal);
            if (version != _browseLoadVersion)
            {
                return;
            }

            var page = await _session.BrowsePageAsync(collection, BrowseFilterText, _browsePage, BrowsePageSize);
            if (version != _browseLoadVersion)
            {
                return;
            }

            _browsePage = page.Page;
            var declared = _browseColumns.Select(c => c.Name).ToList();
            ShowDocuments(page.Documents, declared);
            ExplainText = page.Explain;
            _loadedBrowseCollection = collection;
            BrowseStatusText = page.Status;
            BrowsePageText = page.PageText;
            CanBrowsePrevious = page.HasPrevious;
            CanBrowseNext = page.HasNext;
            StatusText = $"{collection}: {BrowseStatusText}";
            BrowseFilterError = "";
            RefreshCollectionCaptions();
        }
        catch (Exception ex)
        {
            BrowseFilterError = ex.Message;
            StatusText = ex.Message;
        }
        finally
        {
            if (version == _browseLoadVersion)
            {
                _suspendCollectionLoad = false;
            }
        }
    }

    private void ShowDocuments(IReadOnlyList<NuvexaDocument> docs, IReadOnlyList<string>? declaredFields = null)
    {
        var keepId = SelectedRow?.Id;
        var fields = _session.MergeFields(docs, declaredFields);
        var rows = _session.ToGrid(docs, declaredFields);
        ApplyPageRows(rows, fields, keepId);
    }

    public async Task BrowseNodeAsync(ExplorerNode? node)
    {
        var collection = node is null
            ? SelectedCollection
            : node.Kind == "collection" ? node.Name : FindCollectionName(node);
        if (collection is null)
        {
            return;
        }

        await BrowseCollectionAsync(collection);
    }

    private void SelectCollectionInTree(string name)
    {
        var node = Tree.FirstOrDefault(n =>
            n.Kind == "collection" && string.Equals(n.Name, name, StringComparison.Ordinal));
        if (node is null)
        {
            return;
        }

        _selectedNode = node;
        Notify(nameof(SelectedNode));
        TreeSelectionRequested?.Invoke(node);
    }

    private void SelectCollection(string? name, bool loadBrowse)
    {
        _suspendCollectionLoad = !loadBrowse;
        try
        {
            if (name is null)
            {
                ClearCollectionSelection();
            }
            else
            {
                SelectedCollection = name;
            }
        }
        finally
        {
            _suspendCollectionLoad = false;
        }
    }

    private void ClearCollectionSelection()
    {
        _clearingCollection = true;
        try
        {
            SelectedCollection = null;
        }
        finally
        {
            _clearingCollection = false;
        }
    }

    private async Task BrowseSelectedAsync() => await EnsureBrowseVisibleAsync();

    private async Task BrowseCollectionAsync(string? collection, bool resetPage = true)
    {
        if (collection is null)
        {
            await EnsureBrowseVisibleAsync();
            return;
        }

        if (resetPage)
        {
            _browsePage = 0;
        }

        SelectCollection(collection, loadBrowse: false);
        SelectedTabIndex = 1;
        await LoadCollectionAsync(collection);
    }

    private async Task EnsureBrowseVisibleAsync()
    {
        if (!_session.IsOpen)
        {
            return;
        }

        var collection = SelectedCollection is not null && CollectionNames.Contains(SelectedCollection)
            ? SelectedCollection
            : CollectionNames.FirstOrDefault();
        if (collection is null)
        {
            StatusText = "Select a collection first.";
            return;
        }

        SelectCollection(collection, loadBrowse: false);
        Notify(nameof(SelectedCollection));
        Notify(nameof(SelectedCollectionIndex));
        SelectedTabIndex = 1;
        await LoadCollectionAsync(collection);
    }

    private async Task DeleteCollectionAsync()
    {
        if (SelectedCollection is null || !_session.IsOpen)
        {
            StatusText = "Select a collection first.";
            return;
        }

        var name = SelectedCollection;
        if (Dialogs is not null &&
            !await Dialogs.ConfirmAsync("Delete Collection", $"Delete collection '{name}' and all of its records?", "Delete", "Cancel"))
        {
            return;
        }

        try
        {
            await _session.DropCollectionAsync(name);
            if (string.Equals(_loadedBrowseCollection, name, StringComparison.Ordinal))
            {
                _loadedBrowseCollection = null;
            }

            ClearCollectionSelection();
            SelectedRow = null;
            DocumentJson = "";
            await RefreshAsync();
            SelectedTabIndex = 0;
            StatusText = $"Deleted collection '{name}'.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    private async Task EditRecordAsync()
    {
        if (SelectedCollection is null || SelectedRow is null || !CanMutate)
        {
            StatusText = "Select a record first.";
            return;
        }

        try
        {
            var columns = (await ColumnsForEditorAsync(SelectedCollection))
                .Select(c => c with { Default = SelectedRow.Cells[c.Name] })
                .ToList();
            var values = await _shell.PromptRecordAsync(SelectedCollection, columns, "Edit Record", "Save");
            if (values is null)
            {
                return;
            }

            var id = SelectedRow.Id;
            var json = ExplorerSession.DocumentJsonFromCells(id, values, columns);
            await _session.ReplaceDocumentAsync(SelectedCollection, json, id);
            await BrowseCollectionAsync(SelectedCollection);
            SelectedRow = GridRows.FirstOrDefault(r => r.Id == id);
            StatusText = $"Updated record {id}.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            if (Dialogs is not null)
            {
                await Dialogs.AlertAsync("Nuvexa Data Studio", ex.Message);
            }
        }
    }

    private async Task CopyCellAsync()
    {
        if (SelectedRow is null)
        {
            return;
        }

        var field = ContextField;
        var text = field is "_id" or null or ""
            ? SelectedRow.Id
            : SelectedRow.Cells[field];
        await _shell.SetClipboardAsync(text);
        StatusText = string.IsNullOrEmpty(field) ? "Copied _id." : $"Copied '{field}'.";
    }

    private async Task CopyRowAsync()
    {
        if (SelectedRow is null)
        {
            return;
        }

        await _shell.SetClipboardAsync(SelectedRow.Json);
        StatusText = "Copied record JSON.";
    }

    private async Task CopyQueryRowAsync()
    {
        if (SelectedQueryRow is null)
        {
            return;
        }

        await _shell.SetClipboardAsync(SelectedQueryRow.Json);
        StatusText = "Copied query row JSON.";
    }

    private void ClearStructureEditor()
    {
        _structureLoadVersion++;
        StructureColumns.Clear();
        SelectedStructureColumn = null;
        StructureHeading = "";
        StructureSummary = "";
        ShowStructureEditor = false;
        _pendingStructureField = null;
        SchemaSampleRows.Clear();
    }

    private async Task LoadStructureAsync(string? collection)
    {
        var version = ++_structureLoadVersion;
        if (!_session.IsOpen || string.IsNullOrEmpty(collection))
        {
            ClearStructureEditor();
            return;
        }

        IReadOnlyList<TableColumnDefinition> columns;
        try
        {
            columns = await ColumnsForEditorAsync(collection);
        }
        catch (Exception ex)
        {
            if (version != _structureLoadVersion)
            {
                return;
            }

            StatusText = ex.Message;
            return;
        }

        if (version != _structureLoadVersion)
        {
            return;
        }

        var keep = _pendingStructureField ?? SelectedStructureColumn?.Name;
        StructureColumns.Clear();
        foreach (var column in columns)
        {
            StructureColumns.Add(column);
        }

        SelectedStructureColumn = keep is null
            ? StructureColumns.FirstOrDefault()
            : StructureColumns.FirstOrDefault(c => string.Equals(c.Name, keep, StringComparison.Ordinal));
        _pendingStructureField = null;
        StructureHeading = $"Collection: {collection}";
        var records = _session.CollectionCount(collection);
        StructureSummary = StructureColumns.Count == 0
            ? $"{records} record(s). No columns yet — add one to define the table."
            : $"{StructureColumns.Count} column(s) · {records} record(s). Select a column to edit or delete it.";
        ShowStructureEditor = true;
        NotifyCrudCommands();
        _ = LoadSchemaSampleAsync(collection, version);
    }

    private async Task<IReadOnlyList<TableColumnDefinition>> ColumnsForEditorAsync(
        string collection,
        IReadOnlyList<NuvexaDocument>? documents = null)
    {
        var declared = (await _session.GetDeclaredColumnsAsync(collection)).ToList();
        var known = new HashSet<string>(declared.Select(c => c.Name), StringComparer.Ordinal);
        var docs = documents ?? await _session.ListAsync(collection, 50);
        foreach (var field in _session.MergeFields(docs).Where(f => f != "_id" && known.Add(f)))
        {
            declared.Add(new TableColumnDefinition(field, "TEXT", "", Unique: false));
        }

        return declared;
    }

    private void RefreshCollectionCaptions()
    {
        if (!_session.IsOpen)
        {
            return;
        }

        foreach (var node in Tree)
        {
            if (node.Kind != "collection")
            {
                continue;
            }

            node.Detail = _session.CollectionCount(node.Name).ToString();
        }
    }

    private string? FindCollectionName(ExplorerNode node)
    {
        foreach (var collection in Tree)
        {
            if (ReferenceEquals(collection, node) || collection.Children.Contains(node))
            {
                return collection.Name;
            }

            foreach (var group in collection.Children)
            {
                if (group.Children.Contains(node))
                {
                    return collection.Name;
                }
            }
        }

        return null;
    }

    private static string DescribeNode(ExplorerNode node, string? collection) => node.Kind switch
    {
        "collection" => $"Collection: {node.Name}\nRight-click to browse, rename, add a column, create an index, or delete this collection.",
        "group" => $"{node.Name} in {collection}",
        "field" => $"Column: {node.Name}\nRight-click to edit or delete this column.",
        "index" => node.Name == "_id_"
            ? $"Index: {node.Name}\nPrimary key on {collection}. This index cannot be dropped."
            : $"Index: {node.Name}\nOn collection {collection}. Right-click to drop it.",
        _ => node.Name
    };

    private async Task ApplyBrowseFilterAsync()
    {
        if (SelectedCollection is null || !_session.IsOpen)
        {
            return;
        }

        _browsePage = 0;
        await LoadCollectionAsync(SelectedCollection);
    }

    private async Task MoveBrowsePageAsync(int delta)
    {
        if (SelectedCollection is null || !_session.IsOpen)
        {
            return;
        }

        var next = _browsePage + delta;
        if (next < 0)
        {
            return;
        }

        _browsePage = next;
        await LoadCollectionAsync(SelectedCollection);
    }

    private async Task CreateIndexAsync()
    {
        if (SelectedCollection is null || !_session.IsOpen)
        {
            StatusText = "Select a collection first.";
            return;
        }

        var field = SelectedNode?.Kind == "field" ? SelectedNode.Name : null;
        var definition = await _shell.PromptIndexAsync(field);
        if (definition is null)
        {
            return;
        }

        try
        {
            await _session.CreateIndexAsync(SelectedCollection, definition.FieldPath, definition.Name, definition.Unique);
            await RefreshAsync();
            SelectedTabIndex = 0;
            StatusText = definition.Unique
                ? $"Unique index on '{definition.FieldPath}' created."
                : $"Index on '{definition.FieldPath}' created.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            if (Dialogs is not null)
            {
                await Dialogs.AlertAsync("Nuvexa Data Studio", ex.Message);
            }
        }
    }

    private async Task DropIndexAsync()
    {
        if (SelectedCollection is null || !_session.IsOpen)
        {
            StatusText = "Select a collection first.";
            return;
        }

        var name = SelectedNode?.Kind == "index" ? SelectedNode.Name : null;
        if (string.IsNullOrEmpty(name))
        {
            name = await _shell.PromptTextAsync("Index name to drop:");
        }

        if (string.IsNullOrWhiteSpace(name) || name == "_id_")
        {
            StatusText = name == "_id_" ? "The _id_ index cannot be dropped." : "Drop index cancelled.";
            return;
        }

        if (Dialogs is not null &&
            !await Dialogs.ConfirmAsync("Drop Index", $"Drop index '{name}' from '{SelectedCollection}'?", "Drop", "Cancel"))
        {
            return;
        }

        try
        {
            await _session.DropIndexAsync(SelectedCollection, name);
            await RefreshAsync();
            SelectedTabIndex = 0;
            StatusText = $"Dropped index '{name}'.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            if (Dialogs is not null)
            {
                await Dialogs.AlertAsync("Nuvexa Data Studio", ex.Message);
            }
        }
    }

    private void RememberQuery(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        _applyingHistory = true;
        try
        {
            var existing = QueryHistory.FirstOrDefault(q => string.Equals(q, trimmed, StringComparison.Ordinal));
            if (existing is not null)
            {
                QueryHistory.Remove(existing);
            }

            QueryHistory.Insert(0, trimmed);
            while (QueryHistory.Count > 20)
            {
                QueryHistory.RemoveAt(QueryHistory.Count - 1);
            }

            _selectedHistoryItem = trimmed;
            Notify(nameof(SelectedHistoryItem));
            QueryHistoryStore.Save(QueryHistory);
        }
        finally
        {
            _applyingHistory = false;
        }
    }

    public async Task OpenRecentAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!File.Exists(path))
        {
            RemoveRecent(path);
            StatusText = $"Recent file was not found: {path}";
            if (Dialogs is not null)
            {
                await Dialogs.AlertAsync("Nuvexa Data Studio", "That recent file is no longer on disk.");
            }

            return;
        }

        await OpenPathAsync(path);
    }

    public void ClearRecentFiles()
    {
        RecentFiles.Clear();
        RecentFilesStore.Save(RecentFiles);
        RecentFilesChanged?.Invoke();
        Notify(nameof(HasRecentFiles));
        StatusText = "Recent files cleared.";
    }

    private void RememberRecent(string path)
    {
        try
        {
            path = Path.GetFullPath(path);
        }
        catch
        {
            return;
        }

        var existing = RecentFiles.FirstOrDefault(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            RecentFiles.Remove(existing);
        }

        RecentFiles.Insert(0, path);
        while (RecentFiles.Count > 12)
        {
            RecentFiles.RemoveAt(RecentFiles.Count - 1);
        }

        RecentFilesStore.Save(RecentFiles);
        RecentFilesChanged?.Invoke();
        Notify(nameof(HasRecentFiles));
    }

    private void RemoveRecent(string path)
    {
        var existing = RecentFiles.FirstOrDefault(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return;
        }

        RecentFiles.Remove(existing);
        RecentFilesStore.Save(RecentFiles);
        RecentFilesChanged?.Invoke();
        Notify(nameof(HasRecentFiles));
    }

    private async Task RenameCollectionAsync()
    {
        if (SelectedCollection is null || !_session.IsOpen)
        {
            StatusText = "Select a collection first.";
            return;
        }

        var from = SelectedCollection;
        var to = await _shell.PromptTextAsync("New collection name:", from);
        if (string.IsNullOrWhiteSpace(to) || string.Equals(from, to.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        to = to.Trim();
        try
        {
            await _session.RenameCollectionAsync(from, to);
            if (string.Equals(_loadedBrowseCollection, from, StringComparison.Ordinal))
            {
                _loadedBrowseCollection = to;
            }

            await RefreshAsync();
            SelectCollection(to, loadBrowse: SelectedTabIndex == 1);
            StatusText = $"Renamed collection '{from}' to '{to}'.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            if (Dialogs is not null)
            {
                await Dialogs.AlertAsync("Nuvexa Data Studio", ex.Message);
            }
        }
    }
}
