#if VSSDK
using System.Runtime.InteropServices;
using System.Windows.Controls;
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
    private readonly TextBox _query = new() { Text = "db.users.find({}).limit(50)", Height = 48, AcceptsReturn = true };
    private readonly TextBox _results = new() { IsReadOnly = true, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _status = new() { TextWrapping = System.Windows.TextWrapping.Wrap };

    public NuvexaVsControl(NuvexaToolWindow host)
    {
        _host = host;
        _status.Text = host.Status;
        var run = new Button { Content = "Run query", Width = 100, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        run.Click += async (_, _) =>
        {
            try
            {
                await _host.QueryAsync(_query.Text).ConfigureAwait(true);
                Reload();
            }
            catch (Exception ex)
            {
                _status.Text = ex.Message;
            }
        };

        _tree.SelectedItemChanged += async (_, e) =>
        {
            if (e.NewValue is TreeViewItem { Tag: ExplorerNode node })
            {
                await _host.SelectNodeAsync(node).ConfigureAwait(true);
                _results.Text = _host.ExportJson();
                _status.Text = _host.Status;
            }
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new System.Windows.GridLength(240) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
        Grid.SetColumn(_tree, 0);
        var right = new DockPanel();
        DockPanel.SetDock(_query, Dock.Top);
        DockPanel.SetDock(run, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        right.Children.Add(_query);
        right.Children.Add(run);
        right.Children.Add(_status);
        right.Children.Add(_results);
        Grid.SetColumn(right, 1);
        grid.Children.Add(_tree);
        grid.Children.Add(right);
        Content = grid;
        Reload();
    }

    public void Reload()
    {
        _tree.Items.Clear();
        foreach (var node in _host.Tree)
        {
            _tree.Items.Add(ToItem(node));
        }

        _results.Text = _host.ExportJson();
        _status.Text = _host.Status;
        if (_host.SelectedCollection is not null)
        {
            _query.Text = $"db.{_host.SelectedCollection}.find({{}}).limit(200)";
        }
    }

    private static TreeViewItem ToItem(ExplorerNode node)
    {
        var item = new TreeViewItem { Header = node.Name, Tag = node };
        foreach (var child in node.Children)
        {
            item.Items.Add(ToItem(child));
        }

        return item;
    }
}
#endif
