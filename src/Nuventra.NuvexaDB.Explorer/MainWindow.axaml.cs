using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Threading;
using Nuventra.NuvexaDB.Tools;

namespace Nuventra.NuvexaDB.Explorer;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    public MainWindow(MainWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.GridSchemaChanged += RebuildColumns;
    }

    public void OpenFromCommandLine(string path) =>
        _ = ((MainWindowViewModel)DataContext!).OpenPathAsync(path);

    private void RebuildColumns(IReadOnlyList<string> fields)
    {
        Dispatcher.UIThread.Post(() =>
        {
            DocsGrid.Columns.Clear();
            DocsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "_id",
                Binding = new Binding(nameof(DocumentRow.Id)),
                Width = new DataGridLength(160)
            });
            foreach (var field in fields.Where(f => f != "_id"))
            {
                DocsGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = field,
                    Binding = new Binding($"Cells[{field}]"),
                    Width = new DataGridLength(140)
                });
            }
        });
    }
}
