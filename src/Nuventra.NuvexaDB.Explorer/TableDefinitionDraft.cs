using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Nuventra.NuvexaDB.Tools;

namespace Nuventra.NuvexaDB.Explorer;

public sealed class TableFieldRow : INotifyPropertyChanged
{
    private string _name = "";
    private string _type = "INTEGER";
    private string _default = "";
    private bool _unique;

    public static readonly IReadOnlyList<string> Types = TableColumnTypes.All;

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public string Type
    {
        get => _type;
        set => Set(ref _type, TableColumnTypes.Normalize(value));
    }

    public string Default
    {
        get => _default;
        set => Set(ref _default, value ?? "");
    }

    public bool Unique
    {
        get => _unique;
        set => Set(ref _unique, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class TableDefinitionDraft : INotifyPropertyChanged
{
    private string _tableName = "";
    private string _preview = "";
    private string _errorText = "";
    private TableFieldRow? _selectedField;

    public TableDefinitionDraft()
    {
        Fields = [];
        Fields.CollectionChanged += OnFieldsChanged;
        RefreshPreview();
    }

    public ObservableCollection<TableFieldRow> Fields { get; }

    public IReadOnlyList<string> FieldTypes => TableColumnTypes.All;

    public bool HasFields => Fields.Count > 0;

    public string TableName
    {
        get => _tableName;
        set
        {
            if (_tableName == value)
            {
                return;
            }

            _tableName = value;
            OnPropertyChanged();
            RefreshPreview();
        }
    }

    public TableFieldRow? SelectedField
    {
        get => _selectedField;
        set
        {
            if (ReferenceEquals(_selectedField, value))
            {
                return;
            }

            _selectedField = value;
            OnPropertyChanged();
        }
    }

    public string Preview
    {
        get => _preview;
        private set
        {
            if (_preview == value)
            {
                return;
            }

            _preview = value;
            OnPropertyChanged();
        }
    }

    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (_errorText == value)
            {
                return;
            }

            _errorText = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void AddField()
    {
        var row = new TableFieldRow { Name = NextFieldName(), Type = "INTEGER" };
        Fields.Add(row);
        SelectedField = row;
        OnPropertyChanged(nameof(HasFields));
    }

    public void RemoveField()
    {
        if (SelectedField is null)
        {
            return;
        }

        var index = Fields.IndexOf(SelectedField);
        if (index < 0)
        {
            return;
        }

        Fields.RemoveAt(index);
        OnPropertyChanged(nameof(HasFields));
        if (Fields.Count == 0)
        {
            SelectedField = null;
            return;
        }

        SelectedField = Fields[Math.Min(index, Fields.Count - 1)];
    }

    public void MoveSelected(int offset)
    {
        if (SelectedField is null)
        {
            return;
        }

        var index = Fields.IndexOf(SelectedField);
        var next = index + offset;
        if (next < 0 || next >= Fields.Count)
        {
            return;
        }

        Fields.Move(index, next);
        SelectedField = Fields[next];
        RefreshPreview();
    }

    public void MoveSelectedTo(int index)
    {
        if (SelectedField is null || index < 0 || index >= Fields.Count)
        {
            return;
        }

        var current = Fields.IndexOf(SelectedField);
        if (current == index)
        {
            return;
        }

        Fields.Move(current, index);
        SelectedField = Fields[index];
        RefreshPreview();
    }

    public bool TryBuild(out TableDefinition? definition)
    {
        definition = null;
        var name = TableName.Trim();
        if (name.Length == 0)
        {
            ErrorText = "Enter a table name.";
            return false;
        }

        if (name is "_id" || name.StartsWith("__", StringComparison.Ordinal))
        {
            ErrorText = "That table name is reserved.";
            return false;
        }

        var columns = new List<TableColumnDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in Fields)
        {
            var field = row.Name.Trim();
            if (field.Length == 0)
            {
                continue;
            }

            if (field is "_id" || field.StartsWith('$'))
            {
                ErrorText = $"Column '{field}' is not allowed.";
                return false;
            }

            if (!seen.Add(field))
            {
                ErrorText = $"Duplicate column '{field}'.";
                return false;
            }

            columns.Add(new TableColumnDefinition(field, TableColumnTypes.Normalize(row.Type), row.Default, row.Unique));
        }

        ErrorText = "";
        definition = new TableDefinition(name, columns);
        return true;
    }

    private string NextFieldName()
    {
        var n = Fields.Count + 1;
        string name;
        do
        {
            name = "Field" + n;
            n++;
        } while (Fields.Any(f => string.Equals(f.Name, name, StringComparison.Ordinal)));

        return name;
    }

    private void OnFieldsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (TableFieldRow row in e.NewItems)
            {
                row.PropertyChanged += OnFieldPropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (TableFieldRow row in e.OldItems)
            {
                row.PropertyChanged -= OnFieldPropertyChanged;
            }
        }

        RefreshPreview();
        OnPropertyChanged(nameof(HasFields));
    }

    private void OnFieldPropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshPreview();

    private void RefreshPreview()
    {
        var name = string.IsNullOrWhiteSpace(TableName) ? "" : TableName.Trim();
        var sb = new StringBuilder();
        sb.Append("CREATE TABLE \"").Append(name).AppendLine("\" (");
        sb.Append("  \"_id\" TEXT PRIMARY KEY");
        foreach (var row in Fields.Where(f => !string.IsNullOrWhiteSpace(f.Name)))
        {
            sb.AppendLine(",");
            sb.Append("  \"").Append(row.Name.Trim()).Append("\" ").Append(TableColumnTypes.Normalize(row.Type));
            if (row.Unique)
            {
                sb.Append(" UNIQUE");
            }

            if (!string.IsNullOrEmpty(row.Default))
            {
                sb.Append(" DEFAULT ").Append(FormatDefault(row.Type, row.Default));
            }
        }

        sb.AppendLine();
        sb.Append(");");
        Preview = sb.ToString();
    }

    private static string FormatDefault(string type, string value) =>
        TableColumnTypes.Normalize(type) is "INTEGER" or "REAL" or "BOOLEAN"
            ? value
            : "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
