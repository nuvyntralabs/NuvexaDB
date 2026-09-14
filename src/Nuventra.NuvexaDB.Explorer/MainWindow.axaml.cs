using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Nuventra.NuvexaDB.Tools;

namespace Nuventra.NuvexaDB.Explorer;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void OnSystemTheme(object? sender, RoutedEventArgs e) =>
        Application.Current!.RequestedThemeVariant = ThemeVariant.Default;

    private void OnLightTheme(object? sender, RoutedEventArgs e) =>
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;

    private void OnDarkTheme(object? sender, RoutedEventArgs e) =>
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

    public MainWindow(MainWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.GridAboutToReset += () => CancelGridEdit(BrowseGrid);
        viewModel.GridSchemaChanged += fields =>
            RebuildColumns(BrowseGrid, fields, editable: true, viewModel.BrowseColumnTypes);
        viewModel.QuerySchemaChanged += fields => RebuildColumns(QueryGrid, fields, editable: false);
        viewModel.RecentFilesChanged += () => RebuildRecentMenu(viewModel);
        viewModel.TreeSelectionRequested += node => SelectTreeNode(node);
        BrowseGrid.BeginningEdit += OnBrowseBeginningEdit;
        BrowseGrid.CellEditEnded += OnBrowseCellEditEnded;
        AddHandler(DragDrop.DragOverEvent, OnWindowDragOver);
        AddHandler(DragDrop.DropEvent, OnWindowDrop);
        Opened += (_, _) => RebuildRecentMenu(viewModel);
    }

    public void OpenFromCommandLine(string path) =>
        _ = ((MainWindowViewModel)DataContext!).OpenPathAsync(path);

    private void OnQueryEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || e.KeyModifiers != KeyModifiers.Control || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        e.Handled = true;
        var caret = QueryEditor.CaretIndex;
        var items = NqlAssist.Completions(vm.QueryText ?? "", caret, vm.CollectionNames);
        if (items.Count == 0)
        {
            return;
        }

        var prefix = NqlAssist.PrefixBefore(vm.QueryText ?? "", caret);
        var pick = items[0];
        var start = caret - prefix.Length;
        vm.QueryText = (vm.QueryText ?? "").Remove(start, prefix.Length).Insert(start, pick);
        QueryEditor.CaretIndex = start + pick.Length;
    }

    private void OnWindowDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = TryGetDroppedNvx(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnWindowDrop(object? sender, DragEventArgs e)
    {
        var path = TryGetDroppedNvx(e);
        if (path is not null && DataContext is MainWindowViewModel viewModel)
        {
            _ = viewModel.OpenPathAsync(path);
        }

        e.Handled = true;
    }

    private static string? TryGetDroppedNvx(DragEventArgs e)
    {
        var items = e.DataTransfer?.TryGetFiles();
        if (items is null)
        {
            return null;
        }

        foreach (var item in items)
        {
            var path = item.TryGetLocalPath();
            if (path is not null &&
                path.EndsWith(".nvx", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private void OnBrowseSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetBrowseSelection(BrowseGrid.SelectedItems.OfType<DocumentRow>().ToList());
        }
    }

    private string? _browseSortField;
    private bool _browseSortDescending;

    private void OnBrowseSorting(object? sender, DataGridColumnEventArgs e)
    {
        var field = e.Column.Header?.ToString();
        if (string.IsNullOrEmpty(field) || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (string.Equals(_browseSortField, field, StringComparison.Ordinal))
        {
            _browseSortDescending = !_browseSortDescending;
        }
        else
        {
            _browseSortField = field;
            _browseSortDescending = false;
        }

        viewModel.SortBrowsePage(field, _browseSortDescending);
    }

    private void OnBrowseCollectionBoxLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is ComboBox box && DataContext is MainWindowViewModel viewModel)
        {
            box.SelectedIndex = viewModel.SelectedCollectionIndex;
        }
    }

    private void SelectTreeNode(ExplorerNode node)
    {
        void Apply()
        {
            StructureTree.SelectedItem = node;
            if (StructureTree.ContainerFromItem(node) is TreeViewItem item)
            {
                item.IsSelected = true;
                item.BringIntoView();
            }
        }

        Dispatcher.UIThread.Post(Apply, DispatcherPriority.Loaded);
    }

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            return;
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetTreeContext(FindAncestorData<ExplorerNode>(e.Source as Control));
        }
    }

    private void OnTreeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.SetTreeContext(FindAncestorData<ExplorerNode>(e.Source as Control));
        if (!viewModel.HasTreeContextMenu)
        {
            e.Handled = true;
        }
    }

    private void OnStructureGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.EditColumnCommand.Execute(null);
        }
    }

    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        var node = FindAncestorData<ExplorerNode>(e.Source as Control);
        if (DataContext is MainWindowViewModel viewModel)
        {
            _ = viewModel.BrowseNodeAsync(node);
        }
    }

    private void OnBrowsePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (BrowseGrid.CurrentColumn?.Header is string field)
        {
            viewModel.ContextField = field;
        }

        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed &&
            FindAncestorData<DocumentRow>(e.Source as Control) is { } row)
        {
            viewModel.SelectedRow = row;
        }
    }

    private static void OnBrowseBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Column?.Header?.ToString() == "_id")
        {
            e.Cancel = true;
        }
    }

    private void OnBrowseCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit)
        {
            return;
        }

        if (e.Row?.DataContext is not DocumentRow row)
        {
            return;
        }

        var field = e.Column?.Header?.ToString();
        if (string.IsNullOrEmpty(field) || field == "_id")
        {
            return;
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ContextField = field;
            _ = viewModel.CommitGridCellAsync(row, field);
        }
    }

    private static T? FindAncestorData<T>(Control? start) where T : class
    {
        for (var current = start; current is not null; current = current.Parent as Control)
        {
            if (current.DataContext is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static void CancelGridEdit(DataGrid? grid)
    {
        if (grid is null)
        {
            return;
        }

        try
        {
            grid.CancelEdit();
        }
        catch
        {
            // Grid may not be in an edit session.
        }
    }

    private void RebuildRecentMenu(MainWindowViewModel viewModel)
    {
        RecentMenu.Items.Clear();
        if (viewModel.RecentFiles.Count == 0)
        {
            RecentMenu.Items.Add(new MenuItem { Header = "(None)", IsEnabled = false });
            return;
        }

        foreach (var path in viewModel.RecentFiles.ToList())
        {
            var captured = path;
            var item = new MenuItem
            {
                Header = $"{Path.GetFileName(captured)}  —  {Path.GetDirectoryName(captured)}",
                Tag = captured
            };
            item.Click += (_, _) => _ = viewModel.OpenRecentAsync(captured);
            RecentMenu.Items.Add(item);
        }

        RecentMenu.Items.Add(new Separator());
        RecentMenu.Items.Add(new MenuItem
        {
            Header = "Clear Recent",
            Command = viewModel.ClearRecentCommand
        });
    }

    private static void RebuildColumns(
        DataGrid? grid,
        IReadOnlyList<string> fields,
        bool editable,
        IReadOnlyDictionary<string, string>? types = null)
    {
        if (grid is null)
        {
            return;
        }

        void Apply()
        {
            try
            {
                grid.AutoGenerateColumns = false;
                CancelGridEdit(grid);
                grid.Columns.Clear();
                grid.Columns.Add(new DataGridTextColumn
                {
                    Header = "_id",
                    Binding = new Binding(nameof(DocumentRow.Id)),
                    Width = new DataGridLength(160),
                    IsReadOnly = true
                });
                foreach (var field in fields.Where(f => f != "_id"))
                {
                    var type = types is not null && types.TryGetValue(field, out var declared)
                        ? TableColumnTypes.Normalize(declared)
                        : "TEXT";
                    grid.Columns.Add(CreateFieldColumn(field, editable, type));
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Invoke(Apply);
        }
    }

    private static DataGridTemplateColumn CreateFieldColumn(string field, bool editable, string type)
    {
        var boolean = editable && type == "BOOLEAN";
        return new DataGridTemplateColumn
        {
            Header = field,
            Width = new DataGridLength(boolean ? 90 : 140),
            IsReadOnly = !editable || boolean,
            CellTemplate = FieldTemplate(field, editable, type, editing: false),
            CellEditingTemplate = editable && !boolean ? FieldTemplate(field, editable, type, editing: true) : null
        };
    }

    private static FuncDataTemplate<DocumentRow> FieldTemplate(string field, bool editable, string type, bool editing) =>
        new((row, scope) =>
        {
            _ = scope;
            if (row is null)
            {
                return new TextBlock();
            }

            if (editable && type == "BOOLEAN")
            {
                return BooleanCell(row, field);
            }

            if (!editable || !editing)
            {
                return new TextBlock
                {
                    Text = row.Cells[field],
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0)
                };
            }

            if (type == "DATETIME")
            {
                return DateCell(row, field);
            }

            var value = row.Cells[field];
            var json = LooksLikeJson(value);
            var box = new TextBox
            {
                Text = value,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4, 2),
                Background = Brushes.Transparent,
                AcceptsReturn = json,
                TextWrapping = json ? TextWrapping.Wrap : TextWrapping.NoWrap,
                MinHeight = json ? 56 : 0
            };
            box.LostFocus += (_, _) => CommitCell(box, row, field, box.Text ?? "");
            return box;
        }, supportsRecycling: false);

    private static CheckBox BooleanCell(DocumentRow row, string field)
    {
        var box = new CheckBox
        {
            IsChecked = TableColumnTypes.IsTrue(row.Cells[field]),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        box.IsCheckedChanged += (_, _) =>
        {
            if (TopLevel.GetTopLevel(box) is Window { DataContext: MainWindowViewModel { IsReadOnlyMode: true } })
            {
                box.IsChecked = TableColumnTypes.IsTrue(row.Cells[field]);
                return;
            }

            CommitCell(box, row, field, box.IsChecked == true ? "true" : "false");
        };
        return box;
    }

    private static CalendarDatePicker DateCell(DocumentRow row, string field)
    {
        DateTime? selected = TableColumnTypes.TryParseDateTime(row.Cells[field], out var parsed) ? parsed : null;
        var picker = new CalendarDatePicker
        {
            SelectedDate = selected,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        picker.SelectedDateChanged += (_, _) =>
        {
            if (picker.SelectedDate is not { } next)
            {
                return;
            }

            if (TopLevel.GetTopLevel(picker) is Window { DataContext: MainWindowViewModel { IsReadOnlyMode: true } })
            {
                return;
            }

            CommitCell(picker, row, field, TableColumnTypes.FormatDateTime(next));
        };
        return picker;
    }

    private static void CommitCell(Control source, DocumentRow row, string field, string next)
    {
        if (row.Cells[field] == next)
        {
            return;
        }

        row.Cells[field] = next;
        if (TopLevel.GetTopLevel(source) is Window { DataContext: MainWindowViewModel viewModel })
        {
            _ = viewModel.CommitGridCellAsync(row, field);
        }
    }

    private static bool LooksLikeJson(string value)
    {
        var trimmed = value.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }
}
