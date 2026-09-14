using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Nuventra.NuvexaDB.Explorer;

public partial class AggregateBuilderWindow : Window
{
    public static readonly string[] Stages =
    [
        "$match", "$project", "$sort", "$skip", "$limit", "$count", "$group", "$lookup"
    ];

    private readonly List<string> _pipeline = [];
    public string? Result { get; private set; }

    public AggregateBuilderWindow()
    {
        InitializeComponent();
        StageBox.ItemsSource = Stages;
        StageBox.SelectedIndex = 0;
    }

    public static async Task<string?> AskAsync(Window owner, string collection, string? current)
    {
        var dlg = new AggregateBuilderWindow();
        dlg.CollectionBox.Text = collection;
        if (!string.IsNullOrWhiteSpace(current) && current.Contains("aggregate", StringComparison.Ordinal))
        {
            dlg.PreviewBox.Text = current;
        }

        dlg.RefreshPreview();
        await dlg.ShowDialog(owner);
        return dlg.Result;
    }

    private void OnAdd(object? sender, RoutedEventArgs e)
    {
        var stage = StageBox.SelectedItem as string ?? "$match";
        var body = string.IsNullOrWhiteSpace(BodyBox.Text) ? DefaultBody(stage) : BodyBox.Text.Trim();
        _pipeline.Add($"{{ {stage}: {body} }}");
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        var col = string.IsNullOrWhiteSpace(CollectionBox.Text) ? "users" : CollectionBox.Text.Trim();
        PreviewBox.Text = _pipeline.Count == 0
            ? $"db.{col}.aggregate([])"
            : $"db.{col}.aggregate([{string.Join(", ", _pipeline)}])";
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        RefreshPreview();
        Result = PreviewBox.Text;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }

    private static string DefaultBody(string stage) => stage switch
    {
        "$match" => "{ }",
        "$project" => "{ name: 1 }",
        "$sort" => "{ _id: 1 }",
        "$skip" => "0",
        "$limit" => "50",
        "$count" => "\"total\"",
        "$group" => "{ _id: \"$status\", n: { $sum: 1 } }",
        "$lookup" => "{ from: \"users\", localField: \"userId\", foreignField: \"_id\", as: \"user\" }",
        _ => "{ }"
    };
}
