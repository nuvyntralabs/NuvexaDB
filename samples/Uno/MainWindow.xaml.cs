using Microsoft.UI.Xaml;
using NuvexaDB.Samples;

namespace NuvexaDB.Samples.Uno;

public sealed partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private async void OnRun(object sender, RoutedEventArgs e)
    {
        Output.Text = await SampleTour.RunAsync(Path.Combine(AppContext.BaseDirectory, "uno.nvx"));
    }
}
