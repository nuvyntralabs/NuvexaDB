using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;

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
            Title = "Export JSON",
            DefaultExtension = "json"
        });
        return file?.Path.LocalPath;
    }

    public Task<string?> PromptKeyAsync(string message) => UnlockWindow.AskAsync(Window, message);

    public Task<(string Current, string Next)?> PromptChangeKeyAsync() => ChangeKeyWindow.AskAsync(Window);

    public void Exit() => Window.Close();

    private static FilePickerFileType NvxType => new("NuvexaDB") { Patterns = ["*.nvx"] };
}
