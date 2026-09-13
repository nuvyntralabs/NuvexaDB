#if VSSDK
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.VisualStudio.Shell;
using Nuventra.NuvexaDB.Tools;

namespace Nuventra.NuvexaDB.VisualStudio;

[Guid(NuvexaGuids.ToolWindowString)]
public sealed class NuvexaToolWindowPane : ToolWindowPane
{
    public NuvexaToolWindowPane() : this(new NuvexaToolWindow())
    {
    }

    public NuvexaToolWindowPane(NuvexaToolWindow host) : base(null)
    {
        Caption = "NuvexaDB";
        Content = new NuvexaVsControl(host);
    }
}

public sealed class NuvexaVsControl : UserControl
{
    private readonly NuvexaToolWindow _host;
    private readonly TreeView _tree = new();
    private readonly TabControl _tabs = new();
    private readonly TextBox _filter = new() { MinHeight = 24 };
    private readonly DataGrid _browseGrid = CreateGrid();
    private readonly TextBox _browseJson = CreateJsonBox();
    private readonly TextBlock _browseExplain = CreateHint();
    private readonly TextBlock _browseStatus = CreateHint();
    private readonly TextBlock _page = new() { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8, Margin = new Thickness(8, 0, 8, 0) };
    private readonly Button _previous = new() { Content = "Previous", Width = 80, IsEnabled = false };
    private readonly Button _next = new() { Content = "Next", Width = 80, IsEnabled = false };
    private readonly ComboBox _samples = new() { DisplayMemberPath = nameof(ExplorerQuerySample.Title) };
    private readonly TextBox _query = new() { Text = "db.users.find({}).limit(50)", Height = 72, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new System.Windows.Media.FontFamily("Consolas") };
    private readonly DataGrid _queryGrid = CreateGrid();
    private readonly TextBox _queryJson = CreateJsonBox();
    private readonly TextBlock _queryExplain = CreateHint();
    private readonly TextBlock _queryStatus = CreateHint();
    private readonly TextBlock _queryError = new() { Foreground = System.Windows.Media.Brushes.IndianRed, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };

    public NuvexaVsControl(NuvexaToolWindow host)
    {
        _host = host;
        _status.Text = host.Status;
        foreach (var sample in host.QuerySamples)
        {
            _samples.Items.Add(sample);
        }

        _samples.SelectionChanged += (_, _) =>
        {
            if (_samples.SelectedItem is ExplorerQuerySample sample)
            {
                _query.Text = _host.ResolveSample(sample);
            }
        };

        var apply = new Button { Content = "Apply", Width = 72, Margin = new Thickness(8, 0, 0, 0) };
        apply.Click += async (_, _) =>
        {
            try
            {
                await _host.ApplyBrowseFilterAsync(_filter.Text).ConfigureAwait(true);
                Reload(rebuildTree: false);
            }
            catch (Exception ex)
            {
                _browseStatus.Text = ex.Message;
                _status.Text = ex.Message;
            }
        };

        _previous.Click += async (_, _) =>
        {
            await _host.BrowsePreviousAsync().ConfigureAwait(true);
            Reload(rebuildTree: false);
        };
        _next.Click += async (_, _) =>
        {
            await _host.BrowseNextAsync().ConfigureAwait(true);
            Reload(rebuildTree: false);
        };

        var run = new Button { Content = "Execute", Width = 88, HorizontalAlignment = HorizontalAlignment.Left };
        run.Click += async (_, _) => await RunQueryAsync().ConfigureAwait(true);

        _tree.SelectedItemChanged += async (_, e) =>
        {
            if (e.NewValue is TreeViewItem { Tag: ExplorerNode node })
            {
                await _host.SelectNodeAsync(node).ConfigureAwait(true);
                if (node.Kind == "collection")
                {
                    _query.Text = $"db.{node.Name}.find({{}}).limit(200)";
                    _tabs.SelectedIndex = 0;
                }

                Reload(rebuildTree: false);
            }
        };

        _browseGrid.SelectionChanged += (_, _) =>
        {
            _host.SelectRow(_browseGrid.SelectedItem as DocumentRow);
            _browseJson.Text = _host.DocumentJson;
        };
        _queryGrid.SelectionChanged += (_, _) =>
        {
            _host.SelectQueryRow(_queryGrid.SelectedItem as DocumentRow);
            _queryJson.Text = _host.QueryDocumentJson;
        };

        var filterRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var filterLabel = new TextBlock { Text = "Filter:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(filterLabel, Dock.Left);
        DockPanel.SetDock(apply, Dock.Right);
        filterRow.Children.Add(filterLabel);
        filterRow.Children.Add(apply);
        filterRow.Children.Add(_filter);

        var pager = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var pagerButtons = new StackPanel { Orientation = Orientation.Horizontal };
        pagerButtons.Children.Add(_previous);
        pagerButtons.Children.Add(_page);
        pagerButtons.Children.Add(_next);
        DockPanel.SetDock(pagerButtons, Dock.Right);
        pager.Children.Add(pagerButtons);
        pager.Children.Add(_browseStatus);

        var browseJsonLabel = new TextBlock { Text = "Selected record (JSON)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) };
        var browse = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(filterRow, Dock.Top);
        DockPanel.SetDock(pager, Dock.Top);
        DockPanel.SetDock(_browseExplain, Dock.Top);
        DockPanel.SetDock(_browseJson, Dock.Bottom);
        DockPanel.SetDock(browseJsonLabel, Dock.Bottom);
        browse.Children.Add(filterRow);
        browse.Children.Add(pager);
        browse.Children.Add(_browseExplain);
        browse.Children.Add(_browseJson);
        browse.Children.Add(browseJsonLabel);
        browse.Children.Add(_browseGrid);

        var sampleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var sampleLabel = new TextBlock { Text = "Examples:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(sampleLabel, Dock.Left);
        sampleRow.Children.Add(sampleLabel);
        sampleRow.Children.Add(_samples);

        var hint = new TextBlock
        {
            Text = "NQL (Nuvexa Query Language): db.<collection>.find({ ... }).limit(n)",
            Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        var runRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
        runRow.Children.Add(run);
        runRow.Children.Add(hint);

        var queryJsonLabel = new TextBlock { Text = "Selected record (JSON)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) };
        var nql = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(sampleRow, Dock.Top);
        DockPanel.SetDock(_query, Dock.Top);
        DockPanel.SetDock(runRow, Dock.Top);
        DockPanel.SetDock(_queryError, Dock.Top);
        DockPanel.SetDock(_queryExplain, Dock.Top);
        DockPanel.SetDock(_queryStatus, Dock.Top);
        DockPanel.SetDock(_queryJson, Dock.Bottom);
        DockPanel.SetDock(queryJsonLabel, Dock.Bottom);
        nql.Children.Add(sampleRow);
        nql.Children.Add(_query);
        nql.Children.Add(runRow);
        nql.Children.Add(_queryError);
        nql.Children.Add(_queryExplain);
        nql.Children.Add(_queryStatus);
        nql.Children.Add(_queryJson);
        nql.Children.Add(queryJsonLabel);
        nql.Children.Add(_queryGrid);

        _tabs.Items.Add(new TabItem { Header = "Browse Data", Content = browse });
        _tabs.Items.Add(new TabItem { Header = "Execute Query", Content = nql });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var right = new DockPanel { Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(_status, Dock.Bottom);
        right.Children.Add(_status);
        right.Children.Add(_tabs);
        Grid.SetColumn(_tree, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(_tree);
        grid.Children.Add(right);
        Content = grid;
        Reload();
    }

    public void Reload(bool rebuildTree = true)
    {
        if (rebuildTree)
        {
            _tree.Items.Clear();
            foreach (var node in _host.Tree)
            {
                _tree.Items.Add(ToItem(node));
            }
        }
        else
        {
            UpdateHeaders(_tree.Items);
        }

        BindGrid(_browseGrid, _host.Rows, _host.SelectedRow);
        BindGrid(_queryGrid, _host.QueryRows, _host.SelectedQueryRow);
        _browseJson.Text = _host.DocumentJson;
        _queryJson.Text = _host.QueryDocumentJson;
        _status.Text = _host.Status;
        _browseExplain.Text = _host.Explain;
        _browseStatus.Text = _host.BrowseStatus;
        _queryExplain.Text = _host.QueryExplain;
        _queryStatus.Text = _host.QueryStatus;
        _page.Text = string.IsNullOrEmpty(_host.BrowsePageText) ? _host.BrowseStatus : _host.BrowsePageText;
        _previous.IsEnabled = _host.HasPreviousPage;
        _next.IsEnabled = _host.HasNextPage;
        if (!string.Equals(_filter.Text, _host.BrowseFilter, StringComparison.Ordinal))
        {
            _filter.Text = _host.BrowseFilter;
        }
    }

    private async Task RunQueryAsync()
    {
        try
        {
            _queryError.Text = "";
            await _host.QueryAsync(_query.Text).ConfigureAwait(true);
            _tabs.SelectedIndex = 1;
            Reload(rebuildTree: false);
        }
        catch (Exception ex)
        {
            _queryError.Text = ex.Message;
            _status.Text = ex.Message;
        }
    }

    private static DataGrid CreateGrid() => new()
    {
        AutoGenerateColumns = false,
        IsReadOnly = true,
        CanUserResizeColumns = true,
        CanUserSortColumns = true,
        GridLinesVisibility = DataGridGridLinesVisibility.All,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        SelectionMode = DataGridSelectionMode.Single
    };

    private static TextBox CreateJsonBox() => new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        Height = 140,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new System.Windows.Media.FontFamily("Consolas")
    };

    private static TextBlock CreateHint() => new()
    {
        Opacity = 0.8,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8)
    };

    private static void BindGrid(DataGrid grid, IReadOnlyList<DocumentRow> rows, DocumentRow? selected)
    {
        grid.ItemsSource = null;
        grid.Columns.Clear();
        var keys = new List<string>();
        foreach (var row in rows)
        {
            foreach (var key in row.Cells.Keys)
            {
                if (!keys.Contains(key, StringComparer.Ordinal))
                {
                    keys.Add(key);
                }
            }
        }

        if (keys.Remove("_id"))
        {
            keys.Insert(0, "_id");
        }
        else if (rows.Count > 0)
        {
            keys.Insert(0, "_id");
        }

        foreach (var key in keys)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = key,
                Binding = new Binding { Path = new PropertyPath("Cells[(0)]", key) },
                IsReadOnly = true
            });
        }

        grid.ItemsSource = rows;
        if (selected is not null)
        {
            grid.SelectedItem = selected;
        }
        else if (rows.Count > 0)
        {
            grid.SelectedIndex = 0;
        }
    }

    private static void UpdateHeaders(ItemCollection items)
    {
        foreach (var item in items)
        {
            if (item is not TreeViewItem treeItem)
            {
                continue;
            }

            if (treeItem.Tag is ExplorerNode node)
            {
                treeItem.Header = node.Caption;
            }

            UpdateHeaders(treeItem.Items);
        }
    }

    private static TreeViewItem ToItem(ExplorerNode node)
    {
        var item = new TreeViewItem { Header = node.Caption, Tag = node };
        foreach (var child in node.Children)
        {
            item.Items.Add(ToItem(child));
        }

        return item;
    }
}
#endif
