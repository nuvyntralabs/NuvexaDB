using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Nuventra.NuvexaDB.Explorer;

public partial class UnlockWindow : Window
{
    public string? Result { get; private set; }

    public UnlockWindow() => InitializeComponent();

    public static async Task<string?> AskAsync(Window owner, string prompt)
    {
        var dlg = new UnlockWindow();
        dlg.Prompt.Text = prompt;
        await dlg.ShowDialog(owner);
        return dlg.Result;
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        Result = KeyBox.Text;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }
}
