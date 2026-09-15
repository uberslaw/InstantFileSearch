using System.Globalization;
using System.Windows.Data;

namespace InstantFileSearch;

public sealed class PercentToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double percent || values[1] is not double maxWidth)
        {
            return 0d;
        }

        return UiLayout.PercentToWidth(percent, maxWidth);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
