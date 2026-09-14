using Nuventra.NuvexaDB;
using Nuventra.NuvexaDB.Tools;
using Xunit;

namespace Nuventra.NuvexaDB.Tests;

public sealed class ExplorerSessionTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nuvexa-session-" + Guid.NewGuid().ToString("N"));

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // temp cleanup
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task CreateCollection_PersistsColumnsAndTypedDefaults()
    {
        var path = Path.Combine(_dir, "schema.nvx");
        await using var session = new ExplorerSession();
        await session.CreateAsync(path, null);
        await session.CreateCollectionAsync(new TableDefinition("users",
        [
            new TableColumnDefinition("name", "TEXT", "", Unique: false),
            new TableColumnDefinition("age", "INTEGER", "21", Unique: true)
        ]));

        var columns = await session.GetDeclaredColumnsAsync("users");
        Assert.Equal(["name", "age"], columns.Select(c => c.Name).ToList());
        var age = Assert.Single(columns, c => c.Name == "age");
        Assert.Equal("INTEGER", age.Type);
        Assert.True(age.Unique);

        var stats = session.Stats();
        Assert.Equal(1, stats.CollectionCount);
        Assert.Equal(0, stats.DocumentCount);

        var json = session.NewRecordJson(columns);
        Assert.Contains("\"age\":21", json.Replace(" ", "", StringComparison.Ordinal));

        var indexes = await session.ListIndexesAsync("users");
        Assert.Contains(indexes, i => i.Unique && i.FieldPath == "age");

        var id = await session.InsertDocumentAsync("users", json);
        var rows = await session.ListAsync("users");
        var row = Assert.Single(rows);
        Assert.Equal(id, row.Id);
        Assert.Equal("21", row["age"]?.ToString());
        Assert.Equal(1, session.Stats().DocumentCount);
    }

    [Fact]
    public async Task AddColumn_PreservesExistingTypes()
    {
        var path = Path.Combine(_dir, "add-col.nvx");
        await using var session = new ExplorerSession();
        await session.CreateAsync(path, null);
        await session.CreateCollectionAsync(new TableDefinition("items",
        [
            new TableColumnDefinition("sku", "TEXT", "", Unique: false)
        ]));
        await session.AddColumnAsync("items", "qty", "1");

        var columns = await session.GetDeclaredColumnsAsync("items");
        Assert.Equal("TEXT", Assert.Single(columns, c => c.Name == "sku").Type);
        Assert.Equal("TEXT", Assert.Single(columns, c => c.Name == "qty").Type);
        Assert.Equal("1", Assert.Single(columns, c => c.Name == "qty").Default);
    }

    [Fact]
    public void DocumentJsonFromCells_UsesColumnTypes()
    {
        var json = ExplorerSession.DocumentJsonFromCells(null,
            new Dictionary<string, string>
            {
                ["name"] = "Ada",
                ["age"] = "36",
                ["active"] = "true"
            },
            [
                new TableColumnDefinition("name", "TEXT", "", Unique: false),
                new TableColumnDefinition("age", "INTEGER", "0", Unique: false),
                new TableColumnDefinition("active", "BOOLEAN", "false", Unique: false)
            ]);
        var compact = json.Replace(" ", "", StringComparison.Ordinal);
        Assert.Contains("\"name\":\"Ada\"", compact);
        Assert.Contains("\"age\":36", compact);
        Assert.Contains("\"active\":true", compact);
    }

    [Fact]
    public async Task NewRecordJson_InsertsAndListsRow()
    {
        var path = Path.Combine(_dir, "insert-row.nvx");
        await using var session = new ExplorerSession();
        await session.CreateAsync(path, null);
        var columns = new List<TableColumnDefinition>
        {
            new("name", "TEXT", "", Unique: false),
            new("age", "INTEGER", "0", Unique: false)
        };
        await session.CreateCollectionAsync(new TableDefinition("people", columns));

        var json = ExplorerSession.DocumentJsonFromCells(null,
            new Dictionary<string, string> { ["name"] = "Ada", ["age"] = "36" },
            columns);
        var id = await session.InsertDocumentAsync("people", json);

        var rows = await session.ListAsync("people");
        var row = Assert.Single(rows);
        Assert.Equal(id, row.Id);
        Assert.Equal("Ada", row["name"]?.ToString());
        Assert.Equal("36", row["age"]?.ToString());
    }

    [Fact]
    public async Task DropCollection_RemovesNameAndSchema()
    {
        var path = Path.Combine(_dir, "drop.nvx");
        await using var session = new ExplorerSession();
        await session.CreateAsync(path, null);
        await session.CreateCollectionAsync(new TableDefinition("temp",
        [
            new TableColumnDefinition("n", "INTEGER", "1", Unique: false)
        ]));
        await session.InsertDocumentAsync("temp", """{"n":1}""");
        await session.DropCollectionAsync("temp");
        Assert.DoesNotContain("temp", session.Collections());
        Assert.Empty(await session.GetDeclaredColumnsAsync("temp"));
    }

    [Fact]
    public async Task RenameCollection_KeepsRowsAndColumns()
    {
        var path = Path.Combine(_dir, "rename.nvx");
        await using var session = new ExplorerSession();
        await session.CreateAsync(path, null);
        await session.CreateCollectionAsync(new TableDefinition("people",
        [
            new TableColumnDefinition("name", "TEXT", "", Unique: false)
        ]));
        var id = await session.InsertDocumentAsync("people", """{"name":"Ada"}""");

        await session.RenameCollectionAsync("people", "users");

        Assert.DoesNotContain("people", session.Collections());
        Assert.Contains("users", session.Collections());
        Assert.Equal(["name"], (await session.GetDeclaredColumnsAsync("users")).Select(c => c.Name).ToList());
        Assert.Empty(await session.GetDeclaredColumnsAsync("people"));
        var row = Assert.Single(await session.ListAsync("users"));
        Assert.Equal(id, row.Id);
        Assert.Equal("Ada", row["name"]?.ToString());
    }

    [Fact]
    public async Task DropColumn_RemovesSchemaAndDocumentValue()
    {
        var path = Path.Combine(_dir, "drop-col.nvx");
        await using var session = new ExplorerSession();
        await session.CreateAsync(path, null);
        await session.CreateCollectionAsync(new TableDefinition("people",
        [
            new TableColumnDefinition("name", "TEXT", "", Unique: false),
            new TableColumnDefinition("age", "INTEGER", "0", Unique: false)
        ]));
        await session.InsertDocumentAsync("people", """{"name":"Ada","age":36}""");
        await session.DropColumnAsync("people", "age");

        var columns = await session.GetDeclaredColumnsAsync("people");
        Assert.Equal(["name"], columns.Select(c => c.Name).ToList());
        var row = Assert.Single(await session.ListAsync("people"));
        Assert.Null(row["age"]);
        Assert.Equal("Ada", row["name"]?.ToString());
    }

    [Fact]
    public async Task UpdateColumn_RenamesFieldAndKeepsValue()
    {
        var path = Path.Combine(_dir, "rename-col.nvx");
        await using var session = new ExplorerSession();
        await session.CreateAsync(path, null);
        await session.CreateCollectionAsync(new TableDefinition("people",
        [
            new TableColumnDefinition("name", "TEXT", "", Unique: false)
        ]));
        await session.InsertDocumentAsync("people", """{"name":"Ada"}""");
        await session.UpdateColumnAsync("people", "name",
            new TableColumnDefinition("fullName", "TEXT", "", Unique: false));

        var columns = await session.GetDeclaredColumnsAsync("people");
        Assert.Equal(["fullName"], columns.Select(c => c.Name).ToList());
        var row = Assert.Single(await session.ListAsync("people"));
        Assert.Null(row["name"]);
        Assert.Equal("Ada", row["fullName"]?.ToString());
    }

    [Fact]
    public async Task LoadTree_UsesColumnsGroupName()
    {
        var path = Path.Combine(_dir, "tree.nvx");
        await using var session = new ExplorerSession();
        await session.CreateAsync(path, null);
        await session.CreateCollectionAsync(new TableDefinition("people",
        [
            new TableColumnDefinition("name", "TEXT", "", Unique: false)
        ]));

        var tree = await session.LoadTreeAsync();
        var collection = Assert.Single(tree);
        Assert.Contains(collection.Children, n => n.Kind == "group" && n.Name == "Columns");
        Assert.DoesNotContain(collection.Children, n => n.Name == "Fields");
        Assert.Equal("0", collection.Detail);
        Assert.Equal("people  (0)", collection.Caption);

        await session.InsertDocumentAsync("people", """{"name":"Ada"}""");
        collection.Detail = session.CollectionCount("people").ToString();
        Assert.Equal("1", collection.Detail);
        Assert.Equal("people  (1)", collection.Caption);
        Assert.Equal("1", (await session.LoadTreeAsync()).Single().Detail);
    }

    [Fact]
    public async Task FindAsync_AppliesEqualityFilter()
    {
        var path = Path.Combine(_dir, "filter.nvx");
        await using var session = new ExplorerSession();
        await session.CreateAsync(path, null);
        await session.CreateCollectionAsync(new TableDefinition("tickets",
        [
            new TableColumnDefinition("status", "TEXT", "", Unique: false)
        ]));
        await session.InsertDocumentAsync("tickets", """{"status":"open"}""");
        await session.InsertDocumentAsync("tickets", """{"status":"paid"}""");

        var filter = ExplorerSession.NormalizeBrowseFilter("status: paid");
        var rows = await session.FindAsync("tickets", filter);
        var row = Assert.Single(rows);
        Assert.Equal("paid", row["status"]?.ToString());
        Assert.Equal(2, session.CollectionCount("tickets"));
    }

    [Fact]
    public void QuerySamples_ResolveCollectionPlaceholder()
    {
        var page = Assert.Single(ExplorerQuerySample.All, s => s.Title == "Page");
        Assert.Equal("db.tickets.find({}).page(2, 200)", page.Resolve("tickets"));
        Assert.Equal(9, ExplorerQuerySample.All.Count);
    }

    [Fact]
    public async Task BrowsePageAsync_PagesAndExplain()
    {
        var path = Path.Combine(_dir, "browse-page.nvx");
        await using var session = new ExplorerSession();
        await session.CreateAsync(path, null);
        await session.CreateCollectionAsync(new TableDefinition("items",
        [
            new TableColumnDefinition("n", "INTEGER", "", Unique: false)
        ]));
        for (var i = 0; i < 5; i++)
        {
            await session.InsertDocumentAsync("items", $@"{{""n"":{i}}}");
        }

        var first = await session.BrowsePageAsync("items", null, page: 0, pageSize: 2);
        var second = await session.BrowsePageAsync("items", null, page: 1, pageSize: 2);
        var last = await session.BrowsePageAsync("items", null, page: 2, pageSize: 2);
        Assert.Equal(2, first.Documents.Count);
        Assert.True(first.HasNext);
        Assert.False(first.HasPrevious);
        Assert.Equal("Showing 1–2 of 5.", first.Status);
        Assert.Equal("Page 1 of 3", first.PageText);
        Assert.Contains("examined=", first.Explain);
        Assert.Equal(2, second.Documents.Count);
        Assert.True(second.HasPrevious);
        Assert.True(second.HasNext);
        Assert.Single(last.Documents);
        Assert.False(last.HasNext);
        Assert.True(last.HasPrevious);
        Assert.Equal("0", first.Documents[0]["n"]?.ToString());
        Assert.Equal("2", second.Documents[0]["n"]?.ToString());

        var filtered = await session.BrowsePageAsync("items", "n: 4", page: 0, pageSize: 2);
        Assert.Single(filtered.Documents);
        Assert.False(filtered.HasNext);
        Assert.Contains("matching", filtered.Status);
    }

    [Fact]
    public async Task FindAsync_SkipAndLimit_ReturnsLaterPage()
    {
        var path = Path.Combine(_dir, "page.nvx");
        await using var session = new ExplorerSession();
        await session.CreateAsync(path, null);
        await session.CreateCollectionAsync(new TableDefinition("items",
        [
            new TableColumnDefinition("n", "INTEGER", "", Unique: false)
        ]));
        for (var i = 0; i < 5; i++)
        {
            await session.InsertDocumentAsync("items", $@"{{""n"":{i}}}");
        }

        var first = await session.FindAsync("items", "{}", limit: 2, skip: 0);
        var second = await session.FindAsync("items", "{}", limit: 2, skip: 2);
        var last = await session.FindAsync("items", "{}", limit: 2, skip: 4);
        Assert.Equal(2, first.Count);
        Assert.Equal(2, second.Count);
        Assert.Single(last);
        Assert.Equal("0", first[0]["n"]?.ToString());
        Assert.Equal("2", second[0]["n"]?.ToString());
        Assert.Equal("4", last[0]["n"]?.ToString());
        Assert.DoesNotContain(second, d => d.Id == first[0].Id);
    }

    [Fact]
    public void NormalizeBrowseFilter_AcceptsJsonAndShorthand()
    {
        Assert.Equal("{}", ExplorerSession.NormalizeBrowseFilter(""));
        Assert.Equal("{ status: \"paid\" }", ExplorerSession.NormalizeBrowseFilter("{ status: \"paid\" }"));
        Assert.Equal("{ status: \"paid\" }", ExplorerSession.NormalizeBrowseFilter("status: paid"));
        Assert.Equal("{ age: 21 }", ExplorerSession.NormalizeBrowseFilter("age = 21"));
        Assert.Throws<NuvexaException>(() => ExplorerSession.NormalizeBrowseFilter("not-a-filter"));
    }

    [Fact]
    public async Task CreateAndDropIndex_RoundTrip()
    {
        var path = Path.Combine(_dir, "idx.nvx");
        await using var session = new ExplorerSession();
        await session.CreateAsync(path, null);
        await session.CreateCollectionAsync(new TableDefinition("users",
        [
            new TableColumnDefinition("email", "TEXT", "", Unique: false)
        ]));
        await session.InsertDocumentAsync("users", """{"email":"ada@example.com"}""");
        await session.CreateIndexAsync("users", "email", "email_idx");

        var indexes = await session.ListIndexesAsync("users");
        Assert.Contains(indexes, i => i.Name == "email_idx" && i.FieldPath == "email");

        var tree = await session.LoadTreeAsync();
        var indexGroup = tree.Single().Children.Single(n => n.Name == "Indexes");
        Assert.Contains(indexGroup.Children, n => n.Kind == "index" && n.Name == "email_idx");

        var plan = await session.ExplainAsync("users", "{ email: \"ada@example.com\" }");
        Assert.Equal("email_idx", plan.IndexName);

        await session.DropIndexAsync("users", "email_idx");
        indexes = await session.ListIndexesAsync("users");
        Assert.DoesNotContain(indexes, i => i.Name == "email_idx");
        await Assert.ThrowsAsync<NuvexaException>(() => session.DropIndexAsync("users", "_id_"));
    }

    [Fact]
    public async Task ExplainQuery_UsesFindFilter()
    {
        var path = Path.Combine(_dir, "explain.nvx");
        await using var session = new ExplorerSession();
        await session.CreateAsync(path, null);
        await session.CreateCollectionAsync(new TableDefinition("users",
        [
            new TableColumnDefinition("email", "TEXT", "", Unique: false)
        ]));
        await session.CreateIndexAsync("users", "email");
        var plan = await session.ExplainQueryAsync("""db.users.find({ email: "ada@example.com" })""");
        Assert.Equal("users", plan.Collection);
        Assert.Equal("email", plan.IndexName);
        Assert.Contains("examined=", ExplorerSession.FormatExplain(plan));
    }
}
