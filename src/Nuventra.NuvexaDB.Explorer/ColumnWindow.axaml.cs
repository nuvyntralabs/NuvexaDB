using Avalonia.Controls;
using Avalonia.Interactivity;
using Nuventra.NuvexaDB.Tools;

namespace Nuventra.NuvexaDB.Explorer;

public partial class ColumnWindow : Window
{
    public TableColumnDefinition? Result { get; private set; }

    public ColumnWindow() => InitializeComponent();

    public static async Task<TableColumnDefinition?> AskAsync(Window owner, TableColumnDefinition? existing = null)
    {
        var dlg = new ColumnWindow();
        dlg.Load(existing);
        await dlg.ShowDialog(owner);
        return dlg.Result;
    }

    private void Load(TableColumnDefinition? existing)
    {
        TypeBox.ItemsSource = TableColumnTypes.All;
        TypeBox.SelectedItem = TableColumnTypes.Normalize(existing?.Type);
        if (existing is null)
        {
            return;
        }

        Title = "Edit column";
        HintText.Text = "Change the column name, type, or default used for new rows.";
        OkButton.Content = "Save";
        NameBox.Text = existing.Name;
        DefaultBox.Text = existing.Default;
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim();
        if (string.IsNullOrEmpty(name) || name == "_id")
        {
            return;
        }

        Result = new TableColumnDefinition(
            name,
            TableColumnTypes.Normalize(TypeBox.SelectedItem?.ToString()),
            DefaultBox.Text ?? "",
            Unique: false);
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }
}
