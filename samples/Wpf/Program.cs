using System.Windows;
using System.Windows.Controls;
using NuvexaDB.Samples;

namespace NuvexaDB.Samples.Wpf;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Run(new SampleWindow());
    }
}

public sealed class SampleWindow : Window
{
    public SampleWindow()
    {
        Title = "NuvexaDB WPF sample";
        Width = 560;
        Height = 420;
        var output = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var run = new Button { Content = "Run integration tour", Padding = new Thickness(8, 4, 8, 4) };
        run.Click += async (_, _) => output.Text = await SampleTour.RunAsync(Path.Combine(AppContext.BaseDirectory, "wpf.nvx"));

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                run,
                new Border { Height = 8 },
                output
            }
        };
    }

}
