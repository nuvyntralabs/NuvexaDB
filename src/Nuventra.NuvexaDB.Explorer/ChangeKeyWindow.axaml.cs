using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Nuventra.NuvexaDB.Explorer;

public partial class ChangeKeyWindow : Window
{
    public (string Current, string Next)? Result { get; private set; }

    public ChangeKeyWindow() => InitializeComponent();

    public static async Task<(string Current, string Next)?> AskAsync(Window owner)
    {
        var dlg = new ChangeKeyWindow();
        await dlg.ShowDialog(owner);
        return dlg.Result;
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var current = CurrentBox.Text ?? "";
        var next = NextBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(next))
        {
            return;
        }

        Result = (current, next);
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }
}
