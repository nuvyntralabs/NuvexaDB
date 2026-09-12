using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Themes.Fluent;
using Nuventra.NuvexaDB;

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
        var create = new Button { Content = "Create, query, GridFS" };
        create.Click += async (_, _) =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, "desktop.nvx");
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            if (File.Exists(path + "-wal"))
            {
                File.Delete(path + "-wal");
            }

            await using var db = NuvexaDatabase.Create(path, new NuvexaCreateOptions { EncryptionKey = "sample" });
            await db.GetCollection("notes").InsertAsync(NuvexaDocument.Parse("""{"title":"hello from Avalonia","n":1}"""));
            var rows = await db.ExecuteAsync("""db.notes.find({ n: { $gte: 1 } })""");
            await using var input = new MemoryStream("avalonia"u8.ToArray());
            var fileId = await db.Files.UploadAsync("hello.txt", input, chunkSize: 8);
            output.Text = string.Join(Environment.NewLine, rows.Documents.Select(r => r.ToJson()))
                          + Environment.NewLine + "file=" + fileId
                          + Environment.NewLine + "encrypted=" + db.Encrypted;
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
