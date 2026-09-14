using Nuventra.NuvexaDB;

namespace Nuventra.NuvexaDB.Tools;

/// <summary>Shared NQL examples for Explorer, Visual Studio, CLI, and VS Code.</summary>
public sealed record ExplorerQuerySample(string Title, string Template)
{
    public string Resolve(string? collection)
    {
        var col = string.IsNullOrWhiteSpace(collection) ? "users" : collection;
        return Template.Replace("{col}", col, StringComparison.Ordinal);
    }

    public static IReadOnlyList<ExplorerQuerySample> All { get; } =
    [
        new("Find all", "db.{col}.find({}).limit(50)"),
        new("Page", "db.{col}.find({}).page(2, 200)"),
        new("Equality", "db.{col}.find({ status: \"paid\" }).limit(50)"),
        new("Regex", "db.{col}.find({ email: { $regex: \"@gmail.com\" } })"),
        new("Sort", "db.{col}.find({}).sort({ _id: 1 }).limit(50)"),
        new("Count", "db.{col}.aggregate([{ $count: \"total\" }])"),
        new("Lookup", "db.orders.aggregate([{ $lookup: { from: \"customers\", localField: \"customerId\", foreignField: \"_id\", as: \"customer\" } }])"),
        new("Update", "db.{col}.update({ status: \"draft\" }, { $set: { status: \"published\" } })"),
        new("Delete", "db.{col}.delete({ status: \"draft\" })")
    ];
}

public sealed class BrowsePageResult
{
    public const int DefaultPageSize = 200;

    public required IReadOnlyList<NuvexaDocument> Documents { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; } = DefaultPageSize;
    public long CollectionTotal { get; init; }
    public bool Filtered { get; init; }
    public bool HasPrevious { get; init; }
    public bool HasNext { get; init; }
    public string FilterJson { get; init; } = "{}";
    public string Status { get; init; } = "";
    public string PageText { get; init; } = "";
    public string Explain { get; init; } = "";
}
