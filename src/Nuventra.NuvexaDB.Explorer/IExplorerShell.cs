namespace Nuventra.NuvexaDB.Explorer;

/// <summary>Desktop shell services. ViewModels stay free of <c>Avalonia.Controls.Window</c>.</summary>
public interface IExplorerShell
{
    Task<string?> PickOpenNvxAsync();
    Task<string?> PickSaveNvxAsync();
    Task<string?> PickOpenJsonAsync();
    Task<string?> PickSaveJsonAsync();
    Task<string?> PromptKeyAsync(string message);
    Task<(string Current, string Next)?> PromptChangeKeyAsync();
    void Exit();
}
