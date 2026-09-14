using NuvexaDB.Samples;
using Nuventra.NuvexaDB;

namespace NuvexaDB.Samples.Maui;

public partial class MainPage : ContentPage
{
    private string DbPath => Path.Combine(FileSystem.AppDataDirectory, "cache.nvx");

    public MainPage() => InitializeComponent();

    private async void OnTour(object? sender, EventArgs e)
    {
        try
        {
            Output.Text = await SampleTour.RunAsync(DbPath);
        }
        catch (Exception ex)
        {
            Output.Text = ex.Message;
        }
    }

    private void OnOpen(object? sender, EventArgs e)
    {
        try
        {
            using var db = OpenOrCreate(CurrentKey());
            Output.Text = $"Opened {db.GetStats().DocumentCount} documents at {DbPath}.";
        }
        catch (Exception ex)
        {
            Output.Text = ex.Message;
        }
    }

    private async void OnInsert(object? sender, EventArgs e)
    {
        try
        {
            using var db = OpenOrCreate(CurrentKey());
            await db.GetCollection("users").InsertManyAsync([
                NuvexaDocument.Parse("""{"name":"Ada","age":36}"""),
                NuvexaDocument.Parse("""{"name":"Cara","age":21}""")
            ]);
            Output.Text = "Inserted sample users.";
        }
        catch (Exception ex)
        {
            Output.Text = ex.Message;
        }
    }

    private async void OnQuery(object? sender, EventArgs e)
    {
        try
        {
            using var db = OpenOrCreate(CurrentKey());
            var rows = await db.ExecuteAsync("db.users.find({ age: { $gte: 21 } })");
            Output.Text = string.Join(Environment.NewLine, rows.Documents.Select(d => d.ToJson()));
        }
        catch (Exception ex)
        {
            Output.Text = ex.Message;
        }
    }

    private async void OnLinq(object? sender, EventArgs e)
    {
        try
        {
            using var db = OpenOrCreate(CurrentKey());
            var adults = await db.GetCollection<Person>("users").ToListAsync(p => p.Age >= 21);
            Output.Text = string.Join(Environment.NewLine, adults.Select(p => $"{p.Name} ({p.Age})"));
        }
        catch (Exception ex)
        {
            Output.Text = ex.Message;
        }
    }

    private async void OnFiles(object? sender, EventArgs e)
    {
        try
        {
            using var db = OpenOrCreate(CurrentKey());
            await using var input = new MemoryStream("maui-gridfs"u8.ToArray());
            var id = await db.Files.UploadAsync("note.txt", input, chunkSize: 8);
            await using var output = new MemoryStream();
            await db.Files.DownloadAsync(id, output);
            Output.Text = $"GridFS {id}: {System.Text.Encoding.UTF8.GetString(output.ToArray())}";
        }
        catch (Exception ex)
        {
            Output.Text = ex.Message;
        }
    }

    private string? CurrentKey() => string.IsNullOrWhiteSpace(KeyEntry.Text) ? null : KeyEntry.Text;

    private NuvexaDatabase OpenOrCreate(string? key)
    {
        if (File.Exists(DbPath) && NuvexaDatabase.IsEncrypted(DbPath) && key is null)
        {
            throw new NuvexaEncryptionException("Database is encrypted. Enter a key.");
        }

        return File.Exists(DbPath)
            ? NuvexaDatabase.Open(DbPath, new NuvexaOpenOptions { EncryptionKey = key })
            : NuvexaDatabase.Create(DbPath, new NuvexaCreateOptions { EncryptionKey = key }); // format 2
    }

    private sealed class Person
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }
}
