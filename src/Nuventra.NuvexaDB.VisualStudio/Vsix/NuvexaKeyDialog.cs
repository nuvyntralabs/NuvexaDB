#if VSSDK
using System.Windows;
using System.Windows.Controls;

namespace Nuventra.NuvexaDB.VisualStudio;

internal static class NuvexaKeyDialog
{
    public static string? Ask(string prompt)
    {
        var box = new PasswordBox { Margin = new Thickness(0, 8, 0, 8) };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        var window = new Window
        {
            Title = "NuvexaDB",
            Width = 420,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Children =
                {
                    new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap },
                    box,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { ok, cancel }
                    }
                }
            }
        };
        string? result = null;
        ok.Click += (_, _) =>
        {
            result = box.Password;
            window.DialogResult = true;
            window.Close();
        };
        return window.ShowDialog() == true ? result : null;
    }
}
#endif
