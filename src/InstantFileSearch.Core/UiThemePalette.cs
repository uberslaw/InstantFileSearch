namespace InstantFileSearch;

/// <summary>
/// Named chrome brushes for View (pastel blue) and Edit (faint rose).
/// Hex strings match App.xaml; WPF swaps them on MainWindow when Edit is on.
/// Exclusions and other owned windows keep the application (View) dictionary.
/// </summary>
public static class UiThemePalette
{
    public const string BgBrush = nameof(BgBrush);
    public const string PanelBrush = nameof(PanelBrush);
    public const string PanelAltBrush = nameof(PanelAltBrush);
    public const string BorderBrush = nameof(BorderBrush);
    public const string TextBrush = nameof(TextBrush);
    public const string MutedBrush = nameof(MutedBrush);
    public const string AccentBrush = nameof(AccentBrush);
    public const string BarTrackBrush = nameof(BarTrackBrush);
    public const string BarFillBrush = nameof(BarFillBrush);
    public const string ButtonBrush = nameof(ButtonBrush);
    public const string ButtonHoverBrush = nameof(ButtonHoverBrush);
    public const string HoverBrush = nameof(HoverBrush);
    public const string HoverBorderBrush = nameof(HoverBorderBrush);
    public const string DangerBrush = nameof(DangerBrush);
    public const string DangerSoftBrush = nameof(DangerSoftBrush);
    public const string SelectionBrush = nameof(SelectionBrush);
    public const string SelectionTextBrush = nameof(SelectionTextBrush);
    public const string ToolbarBgBrush = nameof(ToolbarBgBrush);
    public const string FileGlyphBrush = nameof(FileGlyphBrush);
    public const string GridLineBrush = nameof(GridLineBrush);
    public const string HeaderBrush = nameof(HeaderBrush);
    public const string InactiveSelectionBrush = nameof(InactiveSelectionBrush);

    public static IReadOnlyDictionary<string, string> View { get; } = new Dictionary<string, string>
    {
        [BgBrush] = "#E8F1FA",
        [PanelBrush] = "#F7FBFE",
        [PanelAltBrush] = "#FFFFFF",
        [BorderBrush] = "#B4C9DC",
        [TextBrush] = "#2C3E50",
        [MutedBrush] = "#4A6278",
        [AccentBrush] = "#2F73C4",
        [BarTrackBrush] = "#C5D8EA",
        [BarFillBrush] = "#2F73C4",
        [ButtonBrush] = "#FFFFFF",
        [ButtonHoverBrush] = "#D4E6F5",
        [HoverBrush] = "#D4E6F5",
        [HoverBorderBrush] = "#7EA4C9",
        [DangerBrush] = "#B03A3A",
        [DangerSoftBrush] = "#B03A3A",
        [SelectionBrush] = "#BFD9F2",
        [SelectionTextBrush] = "#1E3A5F",
        [ToolbarBgBrush] = "#DCEAF6",
        [FileGlyphBrush] = "#5E738A",
        [GridLineBrush] = "#D3E2EF",
        [HeaderBrush] = "#E1EEF7",
        [InactiveSelectionBrush] = "#D7E6F4",
    };

    public static IReadOnlyDictionary<string, string> Edit { get; } = new Dictionary<string, string>
    {
        [BgBrush] = "#FAE8E8",
        [PanelBrush] = "#FEF7F7",
        [PanelAltBrush] = "#FFFFFF",
        [BorderBrush] = "#DCB4B4",
        [TextBrush] = "#2C3E50",
        [MutedBrush] = "#785A5A",
        [AccentBrush] = "#C24E4E",
        [BarTrackBrush] = "#EAC5C5",
        [BarFillBrush] = "#C24E4E",
        [ButtonBrush] = "#FFFFFF",
        [ButtonHoverBrush] = "#F5D4D4",
        [HoverBrush] = "#F5D4D4",
        [HoverBorderBrush] = "#C97E7E",
        [DangerBrush] = "#B03A3A",
        [DangerSoftBrush] = "#B03A3A",
        [SelectionBrush] = "#F2C4C4",
        [SelectionTextBrush] = "#5F1E1E",
        [ToolbarBgBrush] = "#F6DCDC",
        [FileGlyphBrush] = "#8A5E5E",
        [GridLineBrush] = "#EFD3D3",
        [HeaderBrush] = "#F7E1E1",
        [InactiveSelectionBrush] = "#F4D7D7",
    };

    public static IEnumerable<string> ResourceKeys => View.Keys;

    public static IReadOnlyDictionary<string, string> ColorsFor(bool isEditMode) =>
        isEditMode ? Edit : View;

    public static (byte R, byte G, byte B) ParseRgb(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);
        if (hex.Length != 7 || hex[0] != '#')
        {
            throw new FormatException($"Expected #RRGGBB, got '{hex}'.");
        }

        return (
            Convert.ToByte(hex.Substring(1, 2), 16),
            Convert.ToByte(hex.Substring(3, 2), 16),
            Convert.ToByte(hex.Substring(5, 2), 16));
    }
}
