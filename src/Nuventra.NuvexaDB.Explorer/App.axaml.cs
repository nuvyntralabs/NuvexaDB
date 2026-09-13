using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Avalonia.MVVMExpress.Hosting;

namespace Nuventra.NuvexaDB.Explorer;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Console.Error.WriteLine(e.Exception);
            e.Handled = true;
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.DataContext: MainWindowViewModel vm })
            {
                vm.StatusText = e.Exception.Message;
            }
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = Program.AppHost.Services.GetRequiredService<MainWindow>();
            AvaloniaWindowContext.For(window);
            desktop.MainWindow = window;
            var args = desktop.Args ?? [];
            if (args.Length > 0 && File.Exists(args[0]))
            {
                window.OpenFromCommandLine(args[0]);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
