namespace Nuventra.NuvexaDB.Tools;

public sealed record ExplorerNode(string Name, string Kind, IReadOnlyList<ExplorerNode> Children);

public sealed record DocumentRow(string Id, string Json, IReadOnlyDictionary<string, string> Cells);
