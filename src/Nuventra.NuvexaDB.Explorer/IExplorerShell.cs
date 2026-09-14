using Nuventra.NuvexaDB.Tools;

namespace Nuventra.NuvexaDB.Explorer;

/// <summary>Desktop shell services. ViewModels stay free of <c>Avalonia.Controls.Window</c>.</summary>
public interface IExplorerShell
{
    Task<string?> PickOpenNvxAsync();
    Task<string?> PickSaveNvxAsync();
    Task<string?> PickOpenJsonAsync();
    Task<string?> PickSaveJsonAsync();
    Task<string?> PickOpenCsvAsync();
    Task<string?> PickSaveCsvAsync();
    Task<string?> PickSaveBackupAsync();
    Task<string?> PromptKeyAsync(string message);
    Task<string?> PromptTextAsync(string message, string? initial = null);
    Task<(string Current, string Next)?> PromptChangeKeyAsync();
    Task<TableColumnDefinition?> PromptColumnAsync(TableColumnDefinition? existing = null);
    Task<IndexDefinition?> PromptIndexAsync(string? field = null);
    Task<TableDefinition?> PromptTableDefinitionAsync(TableDefinition? existing = null);
    Task<IReadOnlyDictionary<string, string>?> PromptRecordAsync(
        string collection,
        IReadOnlyList<TableColumnDefinition> columns,
        string action = "New Record",
        string confirm = "Insert");
    Task<string?> PromptAggregateAsync(string collection, string? currentNql);
    Task ShowExplainAsync(string title, string body);
    Task SetClipboardAsync(string text);
    Task ShowAboutAsync();
    void Exit();
}
