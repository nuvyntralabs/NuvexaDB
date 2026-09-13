using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Plugin.Avalonia.MVVMExpress.Input;
using Nuventra.NuvexaDB;
using Nuventra.NuvexaDB.Tools;

namespace Nuventra.NuvexaDB.Explorer;

public sealed partial class MainWindowViewModel
{
    private readonly List<DocumentRow> _pageRows = [];
    private readonly List<DocumentRow> _selectedBrowseRows = [];
    private bool _isReadOnlyMode;
    private bool _applyingSavedQuery;
    private string _gridFindText = "";
    private string _browseFindStatus = "";
    private string _filterField = "";
    private string _filterOperator = "equals";
    private string _filterValue = "";
    private SavedQuery? _selectedSavedQuery;

    public ObservableCollection<JsonDocumentNode> DocumentTree { get; } = [];
    public ObservableCollection<SchemaSampleField> SchemaSampleRows { get; } = [];
    public ObservableCollection<string> BrowseFilterFields { get; } = [];
    public ObservableCollection<SavedQuery> SavedQueries { get; } = [];
    public IReadOnlyList<string> FilterOperators => BrowseFilterBuilder.Operators;

    public AsyncModelCommand CloneRecordCommand { get; private set; } = null!;
    public AsyncModelCommand ImportCsvCommand { get; private set; } = null!;
    public AsyncModelCommand ExportCsvCommand { get; private set; } = null!;
    public AsyncModelCommand ExportQueryCsvCommand { get; private set; } = null!;
    public AsyncModelCommand BackupCommand { get; private set; } = null!;
    public AsyncModelCommand RestoreCommand { get; private set; } = null!;
    public AsyncModelCommand ShowPropertiesCommand { get; private set; } = null!;
    public AsyncModelCommand BuildBrowseFilterCommand { get; private set; } = null!;
    public AsyncModelCommand SaveQueryCommand { get; private set; } = null!;
    public AsyncModelCommand DeleteSavedQueryCommand { get; private set; } = null!;
    public ModelCommand ApplyGridFindCommand { get; private set; } = null!;

    public bool IsReadOnlyMode
    {
        get => _isReadOnlyMode;
        set
        {
            if (SetProperty(ref _isReadOnlyMode, value))
            {
                Notify(nameof(CanMutate));
                NotifyCrudCommands();
                StatusText = value
                    ? "Read-only — browsing and queries stay available; writes are disabled."
                    : _session.IsOpen
                        ? "Read-only off. Grid edits and structure changes are enabled."
                        : StatusText;
            }
        }
    }

    public bool CanMutate => _session.IsOpen && !_isReadOnlyMode;

    public string GridFindText
    {
        get => _gridFindText;
        set => SetProperty(ref _gridFindText, value);
    }

    public string BrowseFindStatus
    {
        get => _browseFindStatus;
        private set => SetProperty(ref _browseFindStatus, value);
    }

    public string FilterField
    {
        get => _filterField;
        set => SetProperty(ref _filterField, value);
    }

    public string FilterOperator
    {
        get => _filterOperator;
        set => SetProperty(ref _filterOperator, value);
    }

    public string FilterValue
    {
        get => _filterValue;
        set => SetProperty(ref _filterValue, value);
    }

    public SavedQuery? SelectedSavedQuery
    {
        get => _selectedSavedQuery;
        set
        {
            if (!SetProperty(ref _selectedSavedQuery, value) || value is null || _applyingSavedQuery)
            {
                return;
            }

            QueryText = value.Text;
            DeleteSavedQueryCommand.NotifyCanExecuteChanged();
        }
    }

    public int SelectedBrowseCount => _selectedBrowseRows.Count;

    internal void InitializeWorkbench()
    {
        CloneRecordCommand = new AsyncModelCommand(_ => CloneRecordAsync(), () => CanMutate && SelectedCollection is not null && SelectedRow is not null);
        ImportCsvCommand = new AsyncModelCommand(_ => ImportCsvAsync(), () => CanMutate && SelectedCollection is not null);
        ExportCsvCommand = new AsyncModelCommand(_ => ExportCsvAsync(), () => _session.IsOpen && SelectedCollection is not null);
        ExportQueryCsvCommand = new AsyncModelCommand(_ => ExportQueryCsvAsync(), () => QueryRows.Count > 0);
        BackupCommand = new AsyncModelCommand(_ => BackupAsync(), () => _session.IsOpen);
        RestoreCommand = new AsyncModelCommand(_ => RestoreAsync());
        ShowPropertiesCommand = new AsyncModelCommand(_ => ShowPropertiesAsync(), () => _session.IsOpen);
        BuildBrowseFilterCommand = new AsyncModelCommand(_ => BuildBrowseFilterAsync(), () => _session.IsOpen && SelectedCollection is not null);
        SaveQueryCommand = new AsyncModelCommand(_ => SaveQueryAsync(), () => _session.IsOpen && !string.IsNullOrWhiteSpace(QueryText));
        DeleteSavedQueryCommand = new AsyncModelCommand(_ => DeleteSavedQueryAsync(), () => SelectedSavedQuery is not null);
        ApplyGridFindCommand = new ModelCommand(() => ApplyGridFind());
        foreach (var item in SavedQueryStore.Load())
        {
            SavedQueries.Add(item);
        }
    }

    public void SetBrowseSelection(IReadOnlyList<DocumentRow> rows)
    {
        _selectedBrowseRows.Clear();
        _selectedBrowseRows.AddRange(rows);
        Notify(nameof(SelectedBrowseCount));
        DeleteDocumentCommand.NotifyCanExecuteChanged();
        CloneRecordCommand.NotifyCanExecuteChanged();
    }

    public void SortBrowsePage(string field, bool descending)
    {
        if (_pageRows.Count == 0)
        {
            return;
        }

        IEnumerable<DocumentRow> ordered = field == "_id"
            ? _pageRows.OrderBy(r => r.Id, StringComparer.Ordinal)
            : _pageRows.OrderBy(r => r.Cells[field], StringComparer.Ordinal);
        if (descending)
        {
            ordered = ordered.Reverse();
        }

        ReplacePageRows(ordered.ToList());
        ApplyGridFind();
    }

    internal void RebuildDocumentTree()
    {
        DocumentTree.Clear();
        foreach (var node in JsonDocumentTree.Parse(DocumentJson))
        {
            DocumentTree.Add(node);
        }
    }

    internal void ApplyPageRows(IReadOnlyList<DocumentRow> rows, IReadOnlyList<string> fields, string? keepId)
    {
        ReplacePageRows(rows);
        GridAboutToReset?.Invoke();
        GridRows.Clear();
        GridSchemaChanged?.Invoke(fields);
        var visible = BrowseFilterBuilder.FindInPage(_pageRows, GridFindText);
        foreach (var row in visible)
        {
            GridRows.Add(row);
        }

        SelectedRow = GridRows.FirstOrDefault(r => r.Id == keepId) ?? GridRows.FirstOrDefault();
        DocumentJson = SelectedRow?.Json ?? "";
        RefreshBrowseFindStatus();
    }

    internal void RefreshBrowseFilterFields()
    {
        var keep = FilterField;
        BrowseFilterFields.Clear();
        BrowseFilterFields.Add("_id");
        foreach (var column in _browseColumns.Select(c => c.Name).Where(n => n != "_id"))
        {
            BrowseFilterFields.Add(column);
        }

        FilterField = BrowseFilterFields.Contains(keep) ? keep : BrowseFilterFields.FirstOrDefault() ?? "";
    }

    internal async Task LoadSchemaSampleAsync(string collection, int version)
    {
        try
        {
            var docs = await _session.ListAsync(collection, 200);
            if (version != _structureLoadVersion)
            {
                return;
            }

            var samples = SchemaSampler.FromJsonArray(ExplorerSession.ExportJson(docs));
            SchemaSampleRows.Clear();
            foreach (var sample in samples)
            {
                SchemaSampleRows.Add(sample);
            }
        }
        catch
        {
            if (version == _structureLoadVersion)
            {
                SchemaSampleRows.Clear();
            }
        }
    }

    internal void ClearWorkbenchState()
    {
        _pageRows.Clear();
        _selectedBrowseRows.Clear();
        DocumentTree.Clear();
        SchemaSampleRows.Clear();
        BrowseFilterFields.Clear();
        GridFindText = "";
        BrowseFindStatus = "";
        FilterValue = "";
        Notify(nameof(SelectedBrowseCount));
    }

    internal void NotifyWorkbenchCommands()
    {
        CloneRecordCommand.NotifyCanExecuteChanged();
        ImportCsvCommand.NotifyCanExecuteChanged();
        ExportCsvCommand.NotifyCanExecuteChanged();
        ExportQueryCsvCommand.NotifyCanExecuteChanged();
        BackupCommand.NotifyCanExecuteChanged();
        ShowPropertiesCommand.NotifyCanExecuteChanged();
        BuildBrowseFilterCommand.NotifyCanExecuteChanged();
        SaveQueryCommand.NotifyCanExecuteChanged();
        DeleteSavedQueryCommand.NotifyCanExecuteChanged();
        Notify(nameof(CanMutate));
        Notify(nameof(SelectedBrowseCount));
    }

    internal static string FormatDatabaseProperties(NuvexaStats stats) =>
        ExplorerInfoText.FormatDatabaseProperties(stats);

    internal string? ClosedDatabaseHint(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var wal = path + "-wal";
        return File.Exists(wal)
            ? "Database closed. A leftover WAL file is next to the .nvx. Reopen the database to checkpoint it."
            : null;
    }

    private void ReplacePageRows(IReadOnlyList<DocumentRow> rows)
    {
        _pageRows.Clear();
        _pageRows.AddRange(rows);
    }

    private void ApplyGridFind()
    {
        if (_pageRows.Count == 0 && GridRows.Count == 0)
        {
            BrowseFindStatus = "";
            return;
        }

        var keepId = SelectedRow?.Id;
        var visible = BrowseFilterBuilder.FindInPage(_pageRows, GridFindText);
        GridAboutToReset?.Invoke();
        GridRows.Clear();
        foreach (var row in visible)
        {
            GridRows.Add(row);
        }

        SelectedRow = GridRows.FirstOrDefault(r => r.Id == keepId) ?? GridRows.FirstOrDefault();
        DocumentJson = SelectedRow?.Json ?? "";
        RefreshBrowseFindStatus();
    }

    private void RefreshBrowseFindStatus()
    {
        if (string.IsNullOrWhiteSpace(GridFindText))
        {
            BrowseFindStatus = "";
            return;
        }

        BrowseFindStatus = $"Find: {GridRows.Count} of {_pageRows.Count} on this page.";
    }

    private async Task CloneRecordAsync()
    {
        if (SelectedCollection is null || SelectedRow is null || !CanMutate)
        {
            StatusText = "Select a record to clone.";
            return;
        }

        try
        {
            var node = JsonNode.Parse(SelectedRow.Json) as JsonObject;
            node?.Remove("_id");
            var json = node?.ToJsonString() ?? "{}";
            var collection = SelectedCollection;
            var id = await _session.InsertDocumentAsync(collection, json);
            RefreshCollectionCaptions();
            if (string.IsNullOrWhiteSpace(BrowseFilterText))
            {
                var total = _session.CollectionCount(collection);
                _browsePage = total <= 0 ? 0 : (int)((total - 1) / BrowsePageSize);
            }

            await BrowseCollectionAsync(collection, resetPage: false);
            SelectedRow = GridRows.FirstOrDefault(r => r.Id == id);
            StatusText = $"Cloned record as {id}.";
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

    private async Task ImportCsvAsync()
    {
        if (SelectedCollection is null || !CanMutate)
        {
            return;
        }

        var path = await _shell.PickOpenCsvAsync();
        if (path is null)
        {
            return;
        }

        try
        {
            var json = ExplorerCsv.ToJsonArray(await File.ReadAllTextAsync(path));
            await _session.ImportJsonAsync(SelectedCollection, json);
            await RefreshAsync();
            SelectedTabIndex = 1;
            StatusText = $"Imported CSV into '{SelectedCollection}'.";
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

    private async Task ExportCsvAsync()
    {
        if (SelectedCollection is null || !_session.IsOpen)
        {
            return;
        }

        var docs = await _session.ListAsync(SelectedCollection, 10_000);
        var path = await _shell.PickSaveCsvAsync();
        if (path is null)
        {
            return;
        }

        await File.WriteAllTextAsync(path, ExplorerCsv.FromJsonArray(ExplorerSession.ExportJson(docs)));
        StatusText = $"Exported collection CSV to {Path.GetFileName(path)}.";
    }

    private async Task ExportQueryCsvAsync()
    {
        if (QueryRows.Count == 0)
        {
            StatusText = "No query results to export.";
            return;
        }

        var path = await _shell.PickSaveCsvAsync();
        if (path is null)
        {
            return;
        }

        var json = "[" + string.Join(",", QueryRows.Select(r => r.Json)) + "]";
        await File.WriteAllTextAsync(path, ExplorerCsv.FromJsonArray(json));
        StatusText = $"Exported {QueryRows.Count} query row(s) to CSV file {Path.GetFileName(path)}.";
    }

    private async Task BackupAsync()
    {
        if (!_session.IsOpen)
        {
            return;
        }

        var path = await _shell.PickSaveBackupAsync();
        if (path is null)
        {
            return;
        }

        try
        {
            await _session.BackupAsync(path);
            StatusText = $"Backup written to {Path.GetFileName(path)}.";
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

    private async Task RestoreAsync()
    {
        var backup = await _shell.PickOpenNvxAsync();
        if (backup is null)
        {
            return;
        }

        var dest = await _shell.PickSaveNvxAsync();
        if (dest is null)
        {
            return;
        }

        if (string.Equals(_session.Path, dest, StringComparison.OrdinalIgnoreCase))
        {
            StatusText = "Restore cancelled. Choose a path other than the open database.";
            return;
        }

        var overwrite = File.Exists(dest);
        if (overwrite && Dialogs is not null &&
            !await Dialogs.ConfirmAsync("Restore Database", $"Overwrite '{Path.GetFileName(dest)}'?", "Overwrite", "Cancel"))
        {
            return;
        }

        try
        {
            await NuvexaDatabase.RestoreAsync(backup, dest, overwrite);
            StatusText = $"Restored to {Path.GetFileName(dest)}.";
            if (Dialogs is not null &&
                await Dialogs.ConfirmAsync("Restore Database", "Open the restored file?", "Open", "Stay"))
            {
                await OpenPathAsync(dest);
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

    private async Task ShowPropertiesAsync()
    {
        if (!_session.IsOpen)
        {
            return;
        }

        var text = FormatDatabaseProperties(_session.Stats());
        if (Dialogs is not null)
        {
            await Dialogs.AlertAsync("Database Properties", text);
        }

        StatusText = text.Split('\n')[0];
    }

    private async Task BuildBrowseFilterAsync()
    {
        if (SelectedCollection is null || !_session.IsOpen)
        {
            return;
        }

        try
        {
            BrowseFilterText = BrowseFilterBuilder.Build(FilterField, FilterOperator, FilterValue);
            BrowseFilterError = "";
            await ApplyBrowseFilterAsync();
        }
        catch (Exception ex)
        {
            BrowseFilterError = ex.Message;
            StatusText = ex.Message;
        }
    }

    private async Task SaveQueryAsync()
    {
        var text = QueryText.Trim();
        if (text.Length == 0)
        {
            return;
        }

        var name = await _shell.PromptTextAsync("Name this query:", SelectedCollection ?? "query");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        name = name.Trim();
        _applyingSavedQuery = true;
        try
        {
            var existing = SavedQueries.FirstOrDefault(q => string.Equals(q.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                SavedQueries.Remove(existing);
            }

            var item = new SavedQuery(name, text);
            SavedQueries.Insert(0, item);
            while (SavedQueries.Count > 40)
            {
                SavedQueries.RemoveAt(SavedQueries.Count - 1);
            }

            SavedQueryStore.Save(SavedQueries);
            _selectedSavedQuery = item;
            Notify(nameof(SelectedSavedQuery));
            StatusText = $"Saved query '{name}'.";
        }
        finally
        {
            _applyingSavedQuery = false;
            DeleteSavedQueryCommand.NotifyCanExecuteChanged();
        }
    }

    private Task DeleteSavedQueryAsync()
    {
        if (SelectedSavedQuery is null)
        {
            return Task.CompletedTask;
        }

        SavedQueries.Remove(SelectedSavedQuery);
        SavedQueryStore.Save(SavedQueries);
        SelectedSavedQuery = SavedQueries.FirstOrDefault();
        StatusText = "Saved query removed.";
        return Task.CompletedTask;
    }
}
