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

        if (double.IsNaN(maxWidth) || maxWidth <= 0)
        {
            return 0d;
        }

        return Math.Max(0, Math.Min(maxWidth, maxWidth * Math.Clamp(percent, 0, 100) / 100.0));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
