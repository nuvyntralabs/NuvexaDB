using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Nuventra.NuvexaDB.Tools;

namespace Nuventra.NuvexaDB.Explorer;

public sealed class AvaloniaExplorerShell : IExplorerShell
{
    private readonly IServiceProvider _services;

    public AvaloniaExplorerShell(IServiceProvider services) => _services = services;

    private Window Window => _services.GetRequiredService<MainWindow>();

    public async Task<string?> PickOpenNvxAsync()
    {
        var files = await Window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open NuvexaDB file",
            AllowMultiple = false,
            FileTypeFilter = [NvxType]
        });
        return files.Count == 0 ? null : files[0].Path.LocalPath;
    }

    public async Task<string?> PickSaveNvxAsync()
    {
        var file = await Window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Create NuvexaDB file",
            DefaultExtension = "nvx",
            FileTypeChoices = [NvxType]
        });
        return file?.Path.LocalPath;
    }

    public async Task<string?> PickOpenJsonAsync()
    {
        var files = await Window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import JSON array",
            AllowMultiple = false
        });
        return files.Count == 0 ? null : files[0].Path.LocalPath;
    }

    public async Task<string?> PickSaveJsonAsync()
    {
        var file = await Window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export JSON file",
            SuggestedFileName = "results.json",
            DefaultExtension = "json",
            FileTypeChoices = [JsonType]
        });
        var path = file?.Path.LocalPath;
        return path is null ? null : EnsureJsonExtension(path);
    }

    public async Task<string?> PickOpenCsvAsync()
    {
        var files = await Window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import CSV",
            AllowMultiple = false,
            FileTypeFilter = [CsvType]
        });
        return files.Count == 0 ? null : files[0].Path.LocalPath;
    }

    public async Task<string?> PickSaveCsvAsync()
    {
        var file = await Window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export CSV file",
            SuggestedFileName = "results.csv",
            DefaultExtension = "csv",
            FileTypeChoices = [CsvType]
        });
        var path = file?.Path.LocalPath;
        return path is null ? null : EnsureCsvExtension(path);
    }

    public async Task<string?> PickSaveBackupAsync()
    {
        var file = await Window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Backup NuvexaDB file",
            SuggestedFileName = "backup.nvx",
            DefaultExtension = "nvx",
            FileTypeChoices = [NvxType]
        });
        return file?.Path.LocalPath;
    }

    public Task<string?> PromptKeyAsync(string message) => UnlockWindow.AskAsync(Window, message);

    public Task<string?> PromptTextAsync(string message, string? initial = null) =>
        PromptWindow.AskAsync(Window, message, initial);

    public Task<(string Current, string Next)?> PromptChangeKeyAsync() => ChangeKeyWindow.AskAsync(Window);

    public Task<TableColumnDefinition?> PromptColumnAsync(TableColumnDefinition? existing = null) =>
        ColumnWindow.AskAsync(Window, existing);

    public Task<IndexDefinition?> PromptIndexAsync(string? field = null) =>
        IndexWindow.AskAsync(Window, field);

    public Task<TableDefinition?> PromptTableDefinitionAsync(TableDefinition? existing = null) =>
        TableDefinitionWindow.AskAsync(Window, existing);

    public Task<IReadOnlyDictionary<string, string>?> PromptRecordAsync(
        string collection,
        IReadOnlyList<TableColumnDefinition> columns,
        string action = "New Record",
        string confirm = "Insert") =>
        RecordWindow.AskAsync(Window, collection, columns, action, confirm);

    public async Task SetClipboardAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(Window)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    public Task<string?> PromptAggregateAsync(string collection, string? currentNql) =>
        AggregateBuilderWindow.AskAsync(Window, collection, currentNql);

    public Task ShowExplainAsync(string title, string body) =>
        VisualExplainWindow.ShowAsync(Window, title, body);

    public Task ShowAboutAsync() => AboutWindow.ShowAsync(Window);

    public void Exit() => Window.Close();

    private static FilePickerFileType NvxType => new("NuvexaDB") { Patterns = ["*.nvx"] };

    private static FilePickerFileType JsonType => new("JSON file")
    {
        Patterns = ["*.json"],
        MimeTypes = ["application/json"]
    };

    private static FilePickerFileType CsvType => new("CSV file")
    {
        Patterns = ["*.csv"],
        MimeTypes = ["text/csv"]
    };

    internal static string EnsureJsonExtension(string path) =>
        path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? path : path + ".json";

    internal static string EnsureCsvExtension(string path) =>
        path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? path : path + ".csv";
}
