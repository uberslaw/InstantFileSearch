using InstantFileSearch;

namespace InstantFileSearch.Tests;

public class SearchExpressionTests
{
    [Fact]
    public void SpacesWithoutPlusStayOneContainsTerm()
    {
        var expr = SearchExpression.Parse("cisco zero.txt");
        Assert.False(expr.UsesAnd);
        var term = Assert.Single(expr.Terms);
        Assert.Equal("*cisco zero.txt*", term.Pattern);
        Assert.True(term.Wildcard);
        Assert.False(term.Negate);
    }

    [Fact]
    public void DefaultsAreContainsNameAndCurrentScan()
    {
        var query = new SearchQuery();
        Assert.Equal(SearchTermOperator.Contains, query.TermOperator);
        Assert.Equal(SearchMatchMode.Name, query.Match);
        Assert.Equal(SearchMatchMode.Name, default(SearchMatchMode));
        Assert.Equal(SearchScopeKind.CurrentScan, default(SearchScopeKind));
        Assert.Equal(SearchScopeKind.CurrentScan, SearchScopes.Parse(null));
        Assert.Equal(SearchScopeKind.CurrentScan, SearchScopes.Parse(""));
        Assert.Equal(SearchScopeKind.AllScans, SearchScopes.Parse(SearchScopes.AllScans));
        Assert.Equal(SearchMatchMode.Name, SearchMatchModes.Parse(null));
        Assert.Equal(SearchMatchMode.Name, SearchMatchModes.Parse(SearchMatchModes.Name));
        Assert.Equal(SearchTermOperator.Contains, SearchTermOperators.Parse(null));
        Assert.Equal(SearchTermOperators.Contains, SearchTermOperators.Default);
        Assert.Equal(SearchMatchModes.Name, SearchMatchModes.Default);
        Assert.Equal(SearchScopes.CurrentScan, SearchScopes.Default);
        Assert.Equal("Name / Path", SearchMatchModes.GroupLabel);
    }

    [Theory]
    [InlineData(SearchTermOperator.Contains, "*cmd*", true, false)]
    [InlineData(SearchTermOperator.IsExactly, "cmd", false, false)]
    [InlineData(SearchTermOperator.IsNotExactly, "cmd", false, true)]
    [InlineData(SearchTermOperator.BeginsWith, "cmd*", true, false)]
    [InlineData(SearchTermOperator.EndsWith, "*cmd", true, false)]
    public void OperatorCompilesBareTerm(
        SearchTermOperator op,
        string pattern,
        bool wildcard,
        bool negate)
    {
        var term = Assert.Single(SearchExpression.Parse("cmd", regex: false, op).Terms);
        Assert.Equal(pattern, term.Pattern);
        Assert.Equal(wildcard, term.Wildcard);
        Assert.Equal(negate, term.Negate);
    }

    [Fact]
    public void ExplicitGlobIsNotWrappedByOperator()
    {
        foreach (var op in new[]
                 {
                     SearchTermOperator.Contains,
                     SearchTermOperator.IsExactly,
                     SearchTermOperator.IsNotExactly,
                     SearchTermOperator.BeginsWith,
                     SearchTermOperator.EndsWith,
                 })
        {
            var term = Assert.Single(SearchExpression.Parse("cmd*", regex: false, op).Terms);
            Assert.Equal("cmd*", term.Pattern);
            Assert.True(term.Wildcard);
            Assert.False(term.Negate);
        }
    }

    [Fact]
    public void PathFiltersIgnoreNameOperator()
    {
        var expr = SearchExpression.Parse("path:Incoming cmd", regex: false, SearchTermOperator.IsExactly);
        Assert.Equal("cmd", Assert.Single(expr.Terms).Pattern);
        Assert.False(Assert.Single(expr.Terms).Wildcard);
        Assert.Equal("Incoming", Assert.Single(expr.PathFilters).Pattern);
        Assert.False(Assert.Single(expr.PathFilters).Wildcard);
    }

    [Theory]
    [InlineData("cisco + zero")]
    [InlineData("cisco+zero")]
    [InlineData("  cisco  +  zero  ")]
    public void PlusSplitsAndWrapsBareTermsAsContains(string text)
    {
        var expr = SearchExpression.Parse(text);
        Assert.True(expr.UsesAnd);
        Assert.Equal(["*cisco*", "*zero*"], expr.Terms.Select(term => term.Pattern).ToList());
        Assert.All(expr.Terms, term => Assert.True(term.Wildcard));
    }

    [Fact]
    public void PlusWithIsExactlyKeepsWholeStringTerms()
    {
        var expr = SearchExpression.Parse("cisco + zero", regex: false, SearchTermOperator.IsExactly);
        Assert.True(expr.UsesAnd);
        Assert.Equal(["cisco", "zero"], expr.Terms.Select(term => term.Pattern).ToList());
        Assert.All(expr.Terms, term => Assert.False(term.Wildcard));
        Assert.All(expr.Terms, term => Assert.False(term.Negate));
    }

    [Fact]
    public void PlusWithWildcardsDoesNotRewrap()
    {
        var expr = SearchExpression.Parse("cisco* + *zero*");
        Assert.True(expr.UsesAnd);
        Assert.Equal(["cisco*", "*zero*"], expr.Terms.Select(term => term.Pattern).ToList());
        Assert.All(expr.Terms, term => Assert.True(term.Wildcard));
    }

    [Fact]
    public void QuotedPhraseIsOneTerm()
    {
        var alone = SearchExpression.Parse("\"cisco zero\"");
        Assert.False(alone.UsesAnd);
        Assert.Equal("*cisco zero*", Assert.Single(alone.Terms).Pattern);
        Assert.True(Assert.Single(alone.Terms).Wildcard);

        var andPhrase = SearchExpression.Parse("\"cisco zero\" + log");
        Assert.True(andPhrase.UsesAnd);
        Assert.Equal(["*cisco zero*", "*log*"], andPhrase.Terms.Select(term => term.Pattern).ToList());

        var exact = SearchExpression.Parse("\"cisco zero\"", regex: false, SearchTermOperator.IsExactly);
        Assert.Equal("cisco zero", Assert.Single(exact.Terms).Pattern);
        Assert.False(Assert.Single(exact.Terms).Wildcard);
    }

    [Fact]
    public void QuotedPathColonIsATermNotAFilter()
    {
        var expr = SearchExpression.Parse("\"path:Incoming\"");
        Assert.Equal("*path:Incoming*", Assert.Single(expr.Terms).Pattern);
        Assert.True(Assert.Single(expr.Terms).Wildcard);
        Assert.Empty(expr.PathFilters);
    }

    [Fact]
    public void PathPrefixIsAFilterAndIsCaseInsensitive()
    {
        var expr = SearchExpression.Parse("PATH:Incoming *zip");
        Assert.Equal("*zip", Assert.Single(expr.Terms).Pattern);
        Assert.Equal("Incoming", Assert.Single(expr.PathFilters).Pattern);
        Assert.False(Assert.Single(expr.PathFilters).Wildcard);
    }

    [Fact]
    public void PathPrefixAllowsQuotedValueAndOptionalSpace()
    {
        var quoted = SearchExpression.Parse("path:\"C:\\Program Files\"");
        Assert.Empty(quoted.Terms);
        Assert.Equal(@"C:\Program Files", Assert.Single(quoted.PathFilters).Pattern);

        var spaced = SearchExpression.Parse("path: Incoming");
        Assert.Equal("Incoming", Assert.Single(spaced.PathFilters).Pattern);
    }

    [Fact]
    public void MultiplePathTokensAndTogether()
    {
        var expr = SearchExpression.Parse("cisco + zero path:Incoming path:cisco");
        Assert.True(expr.UsesAnd);
        Assert.Equal(["*cisco*", "*zero*"], expr.Terms.Select(term => term.Pattern).ToList());
        Assert.Equal(["Incoming", "cisco"], expr.PathFilters.Select(filter => filter.Pattern).ToList());
    }

    [Fact]
    public void PathWildcardStaysGlob()
    {
        var expr = SearchExpression.Parse(@"path:*\share\*");
        Assert.True(Assert.Single(expr.PathFilters).Wildcard);
        Assert.Equal(@"*\share\*", Assert.Single(expr.PathFilters).Pattern);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+")]
    [InlineData(" + + ")]
    [InlineData("path:")]
    public void EmptyOrPlusOnlyIsNotText(string? text)
    {
        var expr = SearchExpression.Parse(text);
        Assert.False(expr.HasText);
        Assert.Empty(expr.Terms);
        Assert.Empty(expr.PathFilters);
        Assert.False(new SearchQuery { Text = text ?? "" }.HasCriteria);
    }

    [Fact]
    public void PathOnlyQueryHasCriteria()
    {
        Assert.True(new SearchQuery { Text = "path:Incoming" }.HasCriteria);
        Assert.True(SearchExpression.Parse("path:Incoming").HasText);
    }

    [Fact]
    public void ExtOnlyQueryHasCriteria()
    {
        Assert.True(new SearchQuery { ExtensionsText = "pdf, .DOCX" }.HasCriteria);
        Assert.False(new SearchQuery { ExtensionsText = "tar.gz, ***" }.HasCriteria);
        Assert.True(new SearchQuery { RestrictToCurrentScan = true }.HasCriteria);
    }

    [Fact]
    public void RegexModeKeepsWholePatternAndExtractsPath()
    {
        var expr = SearchExpression.Parse("cisco.*zero path:Incoming", regex: true);
        Assert.True(expr.IsRegex);
        Assert.False(expr.IsRegexInvalid);
        Assert.False(expr.UsesAnd);
        Assert.Empty(expr.Terms);
        Assert.Equal("Incoming", Assert.Single(expr.PathFilters).Pattern);
        Assert.NotNull(expr.CompiledRegex);
        Assert.Matches(expr.CompiledRegex, "cisco-zero-day.txt");
    }

    [Fact]
    public void RegexModeDoesNotTreatPlusAsAnd()
    {
        var expr = SearchExpression.Parse("cisco + zero", regex: true);
        Assert.True(expr.IsRegex);
        Assert.False(expr.UsesAnd);
        Assert.Empty(expr.Terms);
        Assert.NotNull(expr.CompiledRegex);
    }

    [Fact]
    public void RegexModeIgnoresNameOperator()
    {
        var expr = SearchExpression.Parse("cmd", regex: true, SearchTermOperator.IsExactly);
        Assert.True(expr.IsRegex);
        Assert.Empty(expr.Terms);
        Assert.NotNull(expr.CompiledRegex);
    }

    [Fact]
    public void InvalidRegexDoesNotThrow()
    {
        var expr = SearchExpression.Parse("[", regex: true);
        Assert.True(expr.IsRegex);
        Assert.True(expr.IsRegexInvalid);
        Assert.True(expr.HasText);
        Assert.Null(expr.CompiledRegex);
        Assert.True(new SearchQuery { Text = "[", UseRegex = true }.HasInvalidRegex);
        Assert.True(new SearchQuery { Text = "[", UseRegex = true }.HasCriteria);
        Assert.False(new SearchQuery { Text = "[", UseRegex = false }.HasInvalidRegex);
    }

    [Fact]
    public void NormalizeSlashesUnifiesSeparators()
    {
        Assert.Equal(@"foo\bar", SearchExpression.NormalizeSlashes("foo/bar"));
        Assert.Equal(@"\\server\share", SearchExpression.NormalizeSlashes("//server/share"));
    }
}
