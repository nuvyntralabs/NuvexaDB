using System.Windows;
using System.Windows.Controls;
using Nuventra.NuvexaDB;

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
        var run = new Button { Content = "Create, query, fail-closed open", Padding = new Thickness(8, 4, 8, 4) };
        run.Click += async (_, _) => output.Text = await RunAsync();

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

    private static async Task<string> RunAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "wpf.nvx");
        File.Delete(path);
        File.Delete(path + "-wal");

        string body;
        await using (var db = NuvexaDatabase.Create(path, new NuvexaCreateOptions { EncryptionKey = "sample" }))
        {
            await db.GetCollection("notes").InsertAsync(NuvexaDocument.Parse("""{"title":"hello from WPF","n":1}"""));
            var rows = await db.ExecuteAsync("""db.notes.find({ n: { $gte: 1 } })""");
            await using var input = new MemoryStream("wpf"u8.ToArray());
            var fileId = await db.Files.UploadAsync("hello.txt", input, chunkSize: 8);
            body = string.Join(Environment.NewLine, rows.Documents.Select(r => r.ToJson()))
                   + Environment.NewLine + "file=" + fileId
                   + Environment.NewLine + "encrypted=" + db.Encrypted;
        }

        try
        {
            NuvexaDatabase.Open(path);
            return body + Environment.NewLine + "ERROR: open without key should have failed.";
        }
        catch (NuvexaEncryptionException ex)
        {
            return body + Environment.NewLine + "Lib fail-closed: " + ex.Message;
        }
    }
}
