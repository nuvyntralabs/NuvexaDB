namespace Nuventra.NuvexaDB;

public sealed record NuvexaIndexInfo(
    string Name,
    string FieldPath,
    bool Unique,
    IReadOnlyList<string>? Fields = null)
{
    public IReadOnlyList<string> FieldPaths => Fields is { Count: > 0 } ? Fields : [FieldPath];
}
