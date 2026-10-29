using System.Text.RegularExpressions;
using InstantFileSearch;

namespace InstantFileSearch.Tests;

public class UiThemePaletteTests
{
    [Fact]
    public void ColorsFor_maps_edit_flag_to_view_or_rose_palette()
    {
        Assert.Same(UiThemePalette.View, UiThemePalette.ColorsFor(isEditMode: false));
        Assert.Same(UiThemePalette.Edit, UiThemePalette.ColorsFor(isEditMode: true));
    }

    [Fact]
    public void View_and_edit_share_keys_and_differ_on_chrome()
    {
        Assert.Equal(UiThemePalette.View.Keys, UiThemePalette.Edit.Keys);
        Assert.Equal("#E8F1FA", UiThemePalette.View[UiThemePalette.BgBrush]);
        Assert.Equal("#DCEAF6", UiThemePalette.View[UiThemePalette.ToolbarBgBrush]);
        Assert.Equal("#2F73C4", UiThemePalette.View[UiThemePalette.AccentBrush]);
        Assert.Equal("#FAE8E8", UiThemePalette.Edit[UiThemePalette.BgBrush]);
        Assert.Equal("#F6DCDC", UiThemePalette.Edit[UiThemePalette.ToolbarBgBrush]);
        Assert.Equal("#C24E4E", UiThemePalette.Edit[UiThemePalette.AccentBrush]);
        Assert.Equal("#FEF7F7", UiThemePalette.Edit[UiThemePalette.PanelBrush]);
        Assert.NotEqual(
            UiThemePalette.View[UiThemePalette.BgBrush],
            UiThemePalette.Edit[UiThemePalette.BgBrush]);
        Assert.Equal(
            UiThemePalette.View[UiThemePalette.DangerBrush],
            UiThemePalette.Edit[UiThemePalette.DangerBrush]);
        Assert.Equal(
            UiThemePalette.View[UiThemePalette.TextBrush],
            UiThemePalette.Edit[UiThemePalette.TextBrush]);
        Assert.Equal(
            UiThemePalette.View[UiThemePalette.PanelAltBrush],
            UiThemePalette.Edit[UiThemePalette.PanelAltBrush]);

        foreach (var color in UiThemePalette.View.Values.Concat(UiThemePalette.Edit.Values))
        {
            var (r, g, b) = UiThemePalette.ParseRgb(color);
            Assert.Equal(color, $"#{r:X2}{g:X2}{b:X2}");
        }
    }

    [Fact]
    public void ParseRgb_reads_six_digit_hex()
    {
        Assert.Equal((232, 241, 250), UiThemePalette.ParseRgb("#E8F1FA"));
        Assert.Equal((250, 232, 232), UiThemePalette.ParseRgb("#FAE8E8"));
        Assert.Throws<FormatException>(() => UiThemePalette.ParseRgb("#FFF"));
        Assert.Throws<ArgumentException>(() => UiThemePalette.ParseRgb(""));
    }

    [Fact]
    public void AppXaml_view_brushes_match_palette()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "InstantFileSearch", "App.xaml"));
        var matches = Regex.Matches(
            xaml,
            """<SolidColorBrush x:Key="(?<key>[A-Za-z]+)" Color="(?<color>#[0-9A-Fa-f]{6})"/>""");
        var fromXaml = matches.ToDictionary(m => m.Groups["key"].Value, m => m.Groups["color"].Value);
        Assert.Equal(UiThemePalette.View.Count, fromXaml.Count);
        foreach (var (key, color) in UiThemePalette.View)
        {
            Assert.True(fromXaml.ContainsKey(key), $"App.xaml is missing {key}");
            Assert.Equal(color, fromXaml[key]);
        }
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            if (File.Exists(Path.Combine(dir, "InstantFileSearch.slnx")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        throw new DirectoryNotFoundException("Could not find InstantFileSearch.slnx above " + AppContext.BaseDirectory);
    }
}
