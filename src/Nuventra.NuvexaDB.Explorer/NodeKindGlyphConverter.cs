using System.Globalization;
using Avalonia.Data.Converters;

namespace Nuventra.NuvexaDB.Explorer;

public sealed class NodeKindGlyphConverter : IValueConverter
{
    public static readonly NodeKindGlyphConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            "collection" => "N",
            "field" => "#",
            "index" => "◆",
            "group" => "▸",
            _ => "•"
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
