using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Plugin.Avalonia.MVVMExpress.Dialogs;
using Plugin.Avalonia.MVVMExpress.Hosting;
using Nuventra.NuvexaDB.Tools;

namespace Nuventra.NuvexaDB.Explorer;

internal static class Program
{
    public static IHost AppHost { get; private set; } = null!;

    [STAThread]
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.UseAvaloniaMvvmExpress(o => o.UseDialogs());
        builder.Services.AddSingleton<ExplorerSession>();
        builder.Services.AddSingleton<IExplorerShell, AvaloniaExplorerShell>();
        builder.Services.AddTransient<MainWindowViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        AppHost = builder.Build();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
