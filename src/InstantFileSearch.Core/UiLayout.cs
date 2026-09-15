namespace InstantFileSearch;

/// <summary>Pixel math for WPF converters. Kept in Core so Linux tests can lock the contract.</summary>
public static class UiLayout
{
    public static double PercentToWidth(double percent, double maxWidth)
    {
        if (double.IsNaN(percent) || double.IsInfinity(percent) || double.IsNaN(maxWidth) || maxWidth <= 0)
        {
            return 0;
        }

        return Math.Max(0, Math.Min(maxWidth, maxWidth * Math.Clamp(percent, 0, 100) / 100.0));
    }
}
