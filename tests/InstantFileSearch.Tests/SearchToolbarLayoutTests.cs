namespace InstantFileSearch.Tests;

public class SearchToolbarLayoutTests
{
    [Fact]
    public void AdvancedToggleIsGoneAndFiltersAreAlwaysVisible()
    {
        var xaml = Read("src", "InstantFileSearch", "MainWindow.xaml");
        Assert.DoesNotContain("IsAdvancedOpen", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Advanced\"", xaml, StringComparison.Ordinal);
        var wrap = xaml.IndexOf("<WrapPanel Margin=\"0,8,0,-8\">", StringComparison.Ordinal);
        Assert.True(wrap >= 0, "Filter WrapPanel should always be in the tree (no Visibility gate).");
        var wrapEnd = xaml.IndexOf("</WrapPanel>", wrap, StringComparison.Ordinal);
        var wrapBlock = xaml[wrap..wrapEnd];
        Assert.DoesNotContain("Visibility=", wrapBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("IsAdvancedOpen", wrapBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void OperatorComboSitsLeftOfSearchBox()
    {
        var xaml = Read("src", "InstantFileSearch", "MainWindow.xaml");
        var op = xaml.IndexOf("TermOperatorOptions", StringComparison.Ordinal);
        var search = xaml.IndexOf("x:Name=\"SearchBox\"", StringComparison.Ordinal);
        var ext = xaml.IndexOf("Text=\"Ext\"", StringComparison.Ordinal);
        var regex = xaml.IndexOf("Content=\"Regex\"", StringComparison.Ordinal);
        Assert.True(op >= 0 && search > op, "Operator ComboBox must be left of Search.");
        Assert.True(ext > search && regex > ext, "Ext then Regex stay to the right of Search.");
        Assert.Contains("IsTermOperatorEnabled", xaml, StringComparison.Ordinal);
        Assert.Contains("TermOperatorTip", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void NamePathGroupIsLeftOfSizeThenModifiedThenScope()
    {
        var xaml = Read("src", "InstantFileSearch", "MainWindow.xaml");
        var namePath = xaml.IndexOf("MatchModeGroupLabel", StringComparison.Ordinal);
        var size = xaml.IndexOf("Text=\"SIZE\"", StringComparison.Ordinal);
        var modified = xaml.IndexOf("Text=\"MODIFIED\"", StringComparison.Ordinal);
        var scope = xaml.IndexOf("Text=\"SCOPE\"", StringComparison.Ordinal);
        Assert.True(namePath >= 0 && size > namePath, "Name / Path (Match) must sit left of Size.");
        Assert.True(modified > size && scope > modified, "Modified then Scope follow Size.");
        Assert.DoesNotContain("Text=\"MATCH\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewModelDefaultsAreContainsNameAndCurrentScan()
    {
        var vm = Read("src", "InstantFileSearch", "MainViewModel.cs");
        Assert.DoesNotContain("IsAdvancedOpen", vm, StringComparison.Ordinal);
        Assert.Contains("_searchScope = SearchScopes.Default", vm, StringComparison.Ordinal);
        Assert.Contains("_matchMode = SearchMatchModes.Default", vm, StringComparison.Ordinal);
        Assert.Contains("_termOperator = SearchTermOperators.Default", vm, StringComparison.Ordinal);
        Assert.Contains("IsTermOperatorEnabled => !IsRegexMode", vm, StringComparison.Ordinal);
        Assert.Contains("SearchScopes.CurrentScan", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("_searchScope = SearchScopes.AllScans", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("_matchMode = \"Name or path\"", vm, StringComparison.Ordinal);
    }

    private static string Read(params string[] relative)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            if (File.Exists(Path.Combine(dir, "InstantFileSearch.slnx")))
            {
                return File.ReadAllText(Path.Combine(new[] { dir }.Concat(relative).ToArray()));
            }

            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        throw new FileNotFoundException("Could not find InstantFileSearch.slnx above " + AppContext.BaseDirectory);
    }
}
