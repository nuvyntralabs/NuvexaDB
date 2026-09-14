using Nuventra.NuvexaDB;
using Nuventra.NuvexaDB.Explorer;
using Nuventra.NuvexaDB.Tools;
using Xunit;

namespace Nuventra.NuvexaDB.Tests;

public sealed class ExplorerIdeTests
{
    [Fact]
    public void Csv_RoundTrip_PreservesColumns()
    {
        const string json = """[{"_id":"1","status":"paid","age":21},{"_id":"2","status":"open","note":"x,y"}]""";
        var csv = ExplorerCsv.FromJsonArray(json);
        Assert.Contains("status", csv, StringComparison.Ordinal);
        Assert.Contains("\"x,y\"", csv, StringComparison.Ordinal);

        var back = ExplorerCsv.ToJsonArray(csv);
        Assert.Contains("paid", back, StringComparison.Ordinal);
        Assert.Contains("21", back, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonTree_ReadsNestedDocument()
    {
        var tree = JsonDocumentTree.Parse("""{"name":"Ada","addr":{"city":"Pune"},"tags":["a","b"]}""");
        var root = Assert.Single(tree);
        Assert.Contains(root.Children, n => n.Name == "name" && n.Value == "Ada");
        var addr = Assert.Single(root.Children, n => n.Name == "addr");
        Assert.Contains(addr.Children, n => n.Name == "city" && n.Value == "Pune");
        Assert.Equal("[]", JsonDocumentTree.Parse("")?.Count == 0 ? "[]" : "fail");
    }

    [Fact]
    public void JsonTree_InvalidJson_IsSingleNode()
    {
        var node = Assert.Single(JsonDocumentTree.Parse("{not-json"));
        Assert.Equal("(invalid JSON)", node.Name);
    }

    [Fact]
    public void SchemaSample_ReportsCoverage()
    {
        var fields = SchemaSampler.FromJsonArray("""[{"status":"paid"},{"status":"open","age":3}]""");
        var status = Assert.Single(fields, f => f.Name == "status");
        Assert.Equal("string", status.Types);
        Assert.Equal(2, status.Present);
        Assert.Equal(0, status.Missing);
        var age = Assert.Single(fields, f => f.Name == "age");
        Assert.Equal(1, age.Missing);
        Assert.Equal("50%", age.Coverage);
    }

    [Fact]
    public void FilterBuilder_EqualsAndCompare()
    {
        Assert.Equal("status: paid", BrowseFilterBuilder.Build("status", "equals", "paid"));
        Assert.Equal("{ age: { $gt: 21 } }", BrowseFilterBuilder.Build("age", ">", "21"));
        Assert.Contains("$regex", BrowseFilterBuilder.Build("email", "contains", "@gmail.com"), StringComparison.Ordinal);
        Assert.Equal("{ city: { $exists: true } }", BrowseFilterBuilder.Build("city", "exists", ""));
    }

    [Fact]
    public void FindInPage_FiltersCurrentRowsOnly()
    {
        var rows = new[]
        {
            new DocumentRow("a", """{"n":"Ada"}""", new Dictionary<string, string> { ["n"] = "Ada" }),
            new DocumentRow("b", """{"n":"Bob"}""", new Dictionary<string, string> { ["n"] = "Bob" })
        };

        Assert.Equal(2, BrowseFilterBuilder.FindInPage(rows, "").Count);
        Assert.Equal("a", Assert.Single(BrowseFilterBuilder.FindInPage(rows, "ada")).Id);
        Assert.Empty(BrowseFilterBuilder.FindInPage(rows, "zzz"));
    }

    [Fact]
    public void PropertiesText_IncludesPathAndWal()
    {
        var text = ExplorerInfoText.FormatDatabaseProperties(new NuvexaStats
        {
            Path = "/tmp/demo.nvx",
            FileBytes = 12,
            WalBytes = 3,
            Encrypted = true,
            CollectionCount = 2,
            DocumentCount = 9
        });
        Assert.Contains("/tmp/demo.nvx", text, StringComparison.Ordinal);
        Assert.Contains("WAL: 3 bytes", text, StringComparison.Ordinal);
        Assert.Contains("Encrypted: True", text, StringComparison.Ordinal);
    }

    [Fact]
    public void About_NamesAuthorAndLinks()
    {
        var text = NuvexaAbout.PlainText("Data Studio");
        Assert.Contains("Nuvexa Data Studio", text, StringComparison.Ordinal);
        Assert.Contains("Niladri Prasad Padhy", text, StringComparison.Ordinal);
        Assert.Contains("MIT", text, StringComparison.Ordinal);
        Assert.Contains(NuvexaAbout.GitHub, text, StringComparison.Ordinal);
        Assert.Contains(NuvexaAbout.NuGet, text, StringComparison.Ordinal);
        Assert.Equal(2, NuvexaAbout.FormatVersion);
    }
}
