using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Themes.Fluent;
using NuvexaDB.Samples;

namespace NuvexaDB.Samples.AvaloniaApp;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
}

public sealed class App : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        Styles.Add(new FluentTheme());
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new SampleWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

public sealed class SampleWindow : Window
{
    public SampleWindow()
    {
        Title = "NuvexaDB Avalonia sample";
        Width = 560;
        Height = 420;
        var output = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var create = new Button { Content = "Run integration tour" };
        create.Click += async (_, _) =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, "desktop.nvx");
            output.Text = await SampleTour.RunAsync(path);
        };

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children = { create, output }
        };
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
    }
}
