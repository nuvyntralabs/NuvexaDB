using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Nuventra.NuvexaDB.Explorer;

public partial class VisualExplainWindow : Window
{
    public VisualExplainWindow() => InitializeComponent();

    public static async Task ShowAsync(Window owner, string title, string body)
    {
        var dlg = new VisualExplainWindow();
        dlg.TitleBlock.Text = title;
        dlg.BodyBlock.Text = body;
        await dlg.ShowDialog(owner);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
