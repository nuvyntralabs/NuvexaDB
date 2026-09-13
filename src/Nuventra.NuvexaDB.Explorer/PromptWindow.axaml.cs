using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Nuventra.NuvexaDB.Explorer;

public partial class PromptWindow : Window
{
    public string? Result { get; private set; }

    public PromptWindow() => InitializeComponent();

    public static async Task<string?> AskAsync(Window owner, string prompt, string? initial = null)
    {
        var dlg = new PromptWindow();
        dlg.Prompt.Text = prompt;
        dlg.ValueBox.Text = initial ?? "";
        await dlg.ShowDialog(owner);
        return dlg.Result;
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var text = ValueBox.Text?.Trim();
        Result = string.IsNullOrEmpty(text) ? null : text;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }
}
