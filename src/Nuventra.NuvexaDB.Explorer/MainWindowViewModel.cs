using System.Collections.ObjectModel;
using Plugin.Avalonia.MVVMExpress.ComponentModel;
using Plugin.Avalonia.MVVMExpress.Dialogs;
using Plugin.Avalonia.MVVMExpress.Input;
using Plugin.Avalonia.MVVMExpress.Threading;
using Nuventra.NuvexaDB.Tools;

namespace Nuventra.NuvexaDB.Explorer;

public sealed class MainWindowViewModel : PageViewModel
{
    private readonly IExplorerShell _shell;
    private readonly ExplorerSession _session;
    private string _status = "Open a .nvx file to begin.";
    private string _queryText = "db.users.find({}).limit(50)";
    private string _documentJson = "";
    private string _explainText = "";
    private string? _selectedCollection;
    private ExplorerNode? _selectedNode;
    private DocumentRow? _selectedRow;

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
        CloseCommand = new AsyncModelCommand(_ => CloseAsync());
        RunQueryCommand = new AsyncModelCommand(_ => RunQueryAsync(), () => _session.IsOpen);
        ImportCommand = new AsyncModelCommand(_ => ImportAsync(), () => _session.IsOpen && SelectedCollection is not null);
        ExportCommand = new AsyncModelCommand(_ => ExportAsync(), () => _session.IsOpen && SelectedCollection is not null);
        ChangeKeyCommand = new AsyncModelCommand(_ => ChangeKeyAsync(), () => _session.IsOpen);
        CompactCommand = new AsyncModelCommand(_ => CompactAsync(), () => _session.IsOpen);
        RefreshTreeCommand = new AsyncModelCommand(_ => RefreshAsync());
        ExitCommand = new ModelCommand(() => _shell.Exit());
    }

    public ObservableCollection<ExplorerNode> Tree { get; } = [];
    public ObservableCollection<DocumentRow> GridRows { get; } = [];
    public event Action<IReadOnlyList<string>>? GridSchemaChanged;

    public AsyncModelCommand NewCommand { get; }
    public AsyncModelCommand OpenCommand { get; }
    public AsyncModelCommand CloseCommand { get; }
    public AsyncModelCommand RunQueryCommand { get; }
    public AsyncModelCommand ImportCommand { get; }
    public AsyncModelCommand ExportCommand { get; }
    public AsyncModelCommand ChangeKeyCommand { get; }
    public AsyncModelCommand CompactCommand { get; }
    public AsyncModelCommand RefreshTreeCommand { get; }
    public ModelCommand ExitCommand { get; }

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
        set => SetProperty(ref _documentJson, value);
    }

    public string ExplainText
    {
        get => _explainText;
        set => SetProperty(ref _explainText, value);
    }

    public string? SelectedCollection
    {
        get => _selectedCollection;
        set => SetProperty(ref _selectedCollection, value);
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

            if (value.Kind == "collection")
            {
                SelectedCollection = value.Name;
                QueryText = $"db.{value.Name}.find({{}}).limit(200)";
                _ = LoadCollectionAsync(value.Name);
            }
            else if (value.Kind == "index")
            {
                var parent = Tree.FirstOrDefault(n => n.Children.Contains(value));
                if (parent is not null)
                {
                    SelectedCollection = parent.Name;
                    StatusText = $"Index {value.Name} on {parent.Name}";
                }
            }
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
            await RefreshAsync();
        }
        catch (NuvexaEncryptionException)
        {
            StatusText = "Wrong encryption key. The file was not opened.";
            if (Dialogs is not null)
            {
                await Dialogs.AlertAsync("NuvexaDB", "Wrong encryption key. The file was not opened.");
            }
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
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
        await RefreshAsync();
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
        await _session.DisposeAsync();
        Tree.Clear();
        GridRows.Clear();
        DocumentJson = "";
        SelectedCollection = null;
        StatusText = "Closed.";
    }

    private async Task RunQueryAsync()
    {
        if (!_session.IsOpen)
        {
            return;
        }

        try
        {
            var docs = await _session.QueryAsync(QueryText);
            ShowDocuments(docs);
            StatusText = $"{docs.Count} document(s).";
            if (SelectedCollection is not null)
            {
                var plan = await _session.ExplainAsync(SelectedCollection, "{}");
                ExplainText = $"{plan.Strategy} examined={plan.Examined} returned={plan.Returned} index={plan.IndexName}";
            }
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
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

    private async Task RefreshAsync()
    {
        Tree.Clear();
        GridRows.Clear();
        if (!_session.IsOpen)
        {
            return;
        }

        foreach (var node in await _session.LoadTreeAsync())
        {
            Tree.Add(node);
        }

        var stats = _session.Stats();
        StatusText = $"{stats.Path}  docs={stats.DocumentCount}  encrypted={stats.Encrypted}  {stats.FileBytes} bytes";
        if (SelectedCollection is not null && Tree.Any(n => n.Name == SelectedCollection))
        {
            await LoadCollectionAsync(SelectedCollection);
        }
    }

    private async Task LoadCollectionAsync(string collection)
    {
        try
        {
            var docs = await _session.ListAsync(collection);
            ShowDocuments(docs);
            var plan = await _session.ExplainAsync(collection, "{}");
            ExplainText = $"{plan.Strategy} examined={plan.Examined} returned={plan.Returned} index={plan.IndexName}";
            StatusText = $"{collection}: {docs.Count} document(s).";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    private void ShowDocuments(IReadOnlyList<NuvexaDocument> docs)
    {
        var rows = _session.ToGrid(docs);
        GridRows.Clear();
        foreach (var row in rows)
        {
            GridRows.Add(row);
        }

        var fields = rows.Count == 0 ? [] : rows[0].Cells.Keys.ToList();
        GridSchemaChanged?.Invoke(fields);
        DocumentJson = rows.Count == 0 ? "" : rows[0].Json;
    }
}
