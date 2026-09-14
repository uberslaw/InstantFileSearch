using System.Globalization;
using System.Windows.Data;

namespace InstantFileSearch;

public sealed class ByteSizeConverter : IValueConverter
{
    public static ByteSizeConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is long size ? ByteFormatter.ToString(size) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
