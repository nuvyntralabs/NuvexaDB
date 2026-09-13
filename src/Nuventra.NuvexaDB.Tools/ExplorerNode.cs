using System.ComponentModel;

namespace Nuventra.NuvexaDB.Tools;

public sealed class ExplorerNode : INotifyPropertyChanged
{
    private string? _detail;

    public ExplorerNode(string name, string kind, IReadOnlyList<ExplorerNode> children, string? detail = null)
    {
        Name = name;
        Kind = kind;
        Children = children;
        _detail = detail;
    }

    public string Name { get; }
    public string Kind { get; }
    public IReadOnlyList<ExplorerNode> Children { get; }

    public string? Detail
    {
        get => _detail;
        set
        {
            if (_detail == value)
            {
                return;
            }

            _detail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Detail)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Caption)));
        }
    }

    public string Caption => Kind switch
    {
        "collection" => string.IsNullOrEmpty(Detail) ? Name : $"{Name}  ({Detail})",
        "index" => string.IsNullOrEmpty(Detail)
            ? (Name.Contains('(', StringComparison.Ordinal) ? Name : $"{Name}  (index)")
            : $"{Name}  ({Detail})",
        "field" => Name,
        _ => Name
    };

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class CellMap : INotifyPropertyChanged
{
    private readonly Dictionary<string, string> _values;

    public CellMap(IDictionary<string, string> values) =>
        _values = new Dictionary<string, string>(values, StringComparer.Ordinal);

    public IEnumerable<string> Keys => _values.Keys;

    public string this[string key]
    {
        get => _values.TryGetValue(key, out var value) ? value : "";
        set
        {
            var next = value ?? "";
            if (_values.TryGetValue(key, out var current) && current == next)
            {
                return;
            }

            _values[key] = next;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs($"Item[{key}]"));
        }
    }

    public IReadOnlyDictionary<string, string> Snapshot() =>
        new Dictionary<string, string>(_values, StringComparer.Ordinal);

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class DocumentRow : INotifyPropertyChanged
{
    public DocumentRow(string id, string json, IDictionary<string, string> cells)
    {
        Id = id;
        Json = json;
        Cells = new CellMap(cells);
    }

    public string Id { get; }
    public string Json { get; private set; }
    public CellMap Cells { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetJson(string json)
    {
        if (Json == json)
        {
            return;
        }

        Json = json;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Json)));
    }
}
