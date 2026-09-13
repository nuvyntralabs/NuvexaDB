using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Nuventra.NuvexaDB.Explorer;

public sealed record IndexDefinition(string FieldPath, string? Name, bool Unique);

public partial class IndexWindow : Window
{
    public IndexDefinition? Result { get; private set; }

    public IndexWindow() => InitializeComponent();

    public static async Task<IndexDefinition?> AskAsync(Window owner, string? field = null)
    {
        var dlg = new IndexWindow();
        dlg.FieldBox.Text = field ?? "";
        await dlg.ShowDialog(owner);
        return dlg.Result;
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var field = FieldBox.Text?.Trim();
        if (string.IsNullOrEmpty(field) || field == "_id")
        {
            return;
        }

        var name = NameBox.Text?.Trim();
        Result = new IndexDefinition(field, string.IsNullOrEmpty(name) ? null : name, UniqueBox.IsChecked == true);
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }
}
