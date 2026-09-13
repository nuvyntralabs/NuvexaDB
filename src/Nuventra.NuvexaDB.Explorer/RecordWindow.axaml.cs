using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Nuventra.NuvexaDB.Tools;

namespace Nuventra.NuvexaDB.Explorer;

public sealed class RecordFieldRow : INotifyPropertyChanged
{
    private string _value = "";

    public RecordFieldRow(string name, string type, string value)
    {
        Name = name;
        Type = type;
        _value = value;
    }

    public string Name { get; }
    public string Type { get; }

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value)
            {
                return;
            }

            _value = value ?? "";
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class RecordDraft
{
    public RecordDraft(
        string collection,
        IReadOnlyList<TableColumnDefinition> columns,
        string action = "New Record",
        string confirm = "Insert")
    {
        Title = string.IsNullOrWhiteSpace(collection) ? action : $"{action} — {collection}";
        ConfirmLabel = confirm;
        Fields = columns
            .Where(c => !string.IsNullOrWhiteSpace(c.Name) && c.Name != "_id")
            .Select(c => new RecordFieldRow(c.Name, TableColumnTypes.Normalize(c.Type), TableColumnTypes.DisplayDefault(c.Type, c.Default)))
            .ToList();
    }

    public string Title { get; }
    public string ConfirmLabel { get; }
    public IReadOnlyList<RecordFieldRow> Fields { get; }
    public bool HasFields => Fields.Count > 0;

    public IReadOnlyDictionary<string, string> Values() =>
        Fields.ToDictionary(f => f.Name, f => f.Value, StringComparer.Ordinal);
}

public partial class RecordWindow : Window
{
    private readonly RecordDraft _draft;

    public IReadOnlyDictionary<string, string>? Result { get; private set; }

    public RecordWindow() : this("", [])
    {
    }

    public RecordWindow(
        string collection,
        IReadOnlyList<TableColumnDefinition> columns,
        string action = "New Record",
        string confirm = "Insert")
    {
        _draft = new RecordDraft(collection, columns, action, confirm);
        InitializeComponent();
        DataContext = _draft;
        Title = _draft.Title;
    }

    public static async Task<IReadOnlyDictionary<string, string>?> AskAsync(
        Window owner,
        string collection,
        IReadOnlyList<TableColumnDefinition> columns,
        string action = "New Record",
        string confirm = "Insert")
    {
        var dlg = new RecordWindow(collection, columns, action, confirm);
        await dlg.ShowDialog(owner);
        return dlg.Result;
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        Result = _draft.Values();
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }
}
