using Avalonia.Controls;
using Avalonia.Interactivity;
using Nuventra.NuvexaDB.Tools;

namespace Nuventra.NuvexaDB.Explorer;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        TitleText.Text = NuvexaAbout.Studio;
        VersionText.Text = $"{NuvexaAbout.Product} {NuvexaAbout.ProductVersion}   ·   .nvx format {NuvexaAbout.FormatVersion}   ·   {NuvexaAbout.License}";
        SummaryText.Text = NuvexaAbout.Summary;
        AboutText.Text = NuvexaAbout.AboutUs;
        AuthorText.Text = $"Author: {NuvexaAbout.Author}";
        OrgText.Text = $"Organization: {NuvexaAbout.Organization}";
        WebsiteLink.NavigateUri = new Uri(NuvexaAbout.Website);
        GitHubLink.NavigateUri = new Uri(NuvexaAbout.GitHub);
        NuGetLink.NavigateUri = new Uri(NuvexaAbout.NuGet);
    }

    public static Task ShowAsync(Window owner)
    {
        var dlg = new AboutWindow();
        return dlg.ShowDialog(owner);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
