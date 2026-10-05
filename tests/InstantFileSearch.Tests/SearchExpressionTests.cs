using InstantFileSearch;

namespace InstantFileSearch.Tests;

public class SearchExpressionTests
{
    [Fact]
    public void SpacesWithoutPlusStayOneExactTerm()
    {
        var expr = SearchExpression.Parse("cisco zero.txt");
        Assert.False(expr.UsesAnd);
        Assert.Equal("cisco zero.txt", Assert.Single(expr.Terms).Pattern);
        Assert.False(Assert.Single(expr.Terms).Wildcard);
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
        Assert.Equal("cisco zero", Assert.Single(alone.Terms).Pattern);
        Assert.False(Assert.Single(alone.Terms).Wildcard);

        var andPhrase = SearchExpression.Parse("\"cisco zero\" + log");
        Assert.True(andPhrase.UsesAnd);
        Assert.Equal(["*cisco zero*", "*log*"], andPhrase.Terms.Select(term => term.Pattern).ToList());
    }

    [Fact]
    public void QuotedPathColonIsATermNotAFilter()
    {
        var expr = SearchExpression.Parse("\"path:Incoming\"");
        Assert.Equal("path:Incoming", Assert.Single(expr.Terms).Pattern);
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
    public void NormalizeSlashesUnifiesSeparators()
    {
        Assert.Equal(@"foo\bar", SearchExpression.NormalizeSlashes("foo/bar"));
        Assert.Equal(@"\\server\share", SearchExpression.NormalizeSlashes("//server/share"));
    }
}
