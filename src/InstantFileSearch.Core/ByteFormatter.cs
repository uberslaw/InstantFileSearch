using System.Globalization;

namespace InstantFileSearch;

public static class ByteFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string ToString(long bytes)
    {
        if (bytes < 0)
        {
            return "-" + ToString(-bytes);
        }

        if (bytes < 1024)
        {
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var format = value >= 100 ? "0" : value >= 10 ? "0.0" : "0.00";
        return value.ToString(format, CultureInfo.InvariantCulture) + " " + Units[unit];
    }

    public static long ToBytes(double amount, ByteSizeUnit unit)
    {
        if (double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0)
        {
            return 0;
        }

        var bytes = amount * Math.Pow(1024, (int)unit);
        if (bytes >= long.MaxValue)
        {
            return long.MaxValue;
        }

        return (long)Math.Round(bytes, MidpointRounding.AwayFromZero);
    }

    public static long? ParseOptionalBytes(string? text, ByteSizeUnit unit)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)
            && !double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out amount))
        {
            return null;
        }

        if (amount < 0)
        {
            return null;
        }

        return ToBytes(amount, unit);
    }

    public static ByteSizeUnit ParseUnit(string? name, ByteSizeUnit fallback = ByteSizeUnit.MB)
    {
        return Enum.TryParse<ByteSizeUnit>(name, ignoreCase: true, out var unit)
               && Enum.IsDefined(unit)
            ? unit
            : fallback;
    }
}
