using Microsoft.UI.Xaml;
using Nuventra.NuvexaDB;

namespace NuvexaDB.Samples.Uno;

public sealed partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private async void OnRun(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "uno.nvx");
        File.Delete(path);
        File.Delete(path + "-wal");

        string body;
        await using (var db = NuvexaDatabase.Create(path, new NuvexaCreateOptions { EncryptionKey = "sample" }))
        {
            await db.GetCollection("notes").InsertAsync(NuvexaDocument.Parse("""{"title":"hello from Uno","n":1}"""));
            var rows = await db.ExecuteAsync("""db.notes.find({ n: { $gte: 1 } })""");
            await using var input = new MemoryStream("uno"u8.ToArray());
            var fileId = await db.Files.UploadAsync("hello.txt", input, chunkSize: 8);
            body = string.Join(Environment.NewLine, rows.Documents.Select(r => r.ToJson()))
                   + Environment.NewLine + "file=" + fileId
                   + Environment.NewLine + "encrypted=" + db.Encrypted;
        }

        try
        {
            NuvexaDatabase.Open(path);
            Output.Text = body + Environment.NewLine + "ERROR: open without key should have failed.";
        }
        catch (NuvexaEncryptionException ex)
        {
            Output.Text = body + Environment.NewLine + "Lib fail-closed: " + ex.Message;
        }
    }
}
