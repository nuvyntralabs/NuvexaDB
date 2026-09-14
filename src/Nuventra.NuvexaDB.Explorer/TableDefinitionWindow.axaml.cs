using Avalonia.Controls;
using Avalonia.Interactivity;
using Nuventra.NuvexaDB.Tools;

namespace Nuventra.NuvexaDB.Explorer;

public partial class TableDefinitionWindow : Window
{
    private readonly TableDefinitionDraft _draft = new();

    public TableDefinition? Result { get; private set; }

    public TableDefinitionWindow()
    {
        InitializeComponent();
        DataContext = _draft;
    }

    public static async Task<TableDefinition?> AskAsync(Window owner, TableDefinition? existing = null)
    {
        var dlg = new TableDefinitionWindow();
        if (existing is not null)
        {
            dlg._draft.Load(existing, nameReadOnly: true);
        }

        await dlg.ShowDialog(owner);
        return dlg.Result;
    }

    private void OnAdd(object? sender, RoutedEventArgs e) => _draft.AddField();

    private void OnRemove(object? sender, RoutedEventArgs e) => _draft.RemoveField();

    private void OnMoveTop(object? sender, RoutedEventArgs e) => _draft.MoveSelectedTo(0);

    private void OnMoveUp(object? sender, RoutedEventArgs e) => _draft.MoveSelected(-1);

    private void OnMoveDown(object? sender, RoutedEventArgs e) => _draft.MoveSelected(1);

    private void OnMoveBottom(object? sender, RoutedEventArgs e) =>
        _draft.MoveSelectedTo(Math.Max(0, _draft.Fields.Count - 1));

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        if (!_draft.TryBuild(out var definition) || definition is null)
        {
            return;
        }

        Result = definition;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }
}
