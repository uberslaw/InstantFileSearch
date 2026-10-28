using InstantFileSearch;

namespace InstantFileSearch.Tests;

public class ExtensionFilterTests
{
    [Fact]
    public void PdfAndDocxMatchIgnoreCaseAndOptionalDots()
    {
        Assert.Equal(["pdf", "DOCX"], ExtensionFilter.Parse("pdf, .DOCX"));
        Assert.Equal(["pdf", "docx", "zip", "exe"], ExtensionFilter.Parse("pdf,docx,.zip exe"));
        Assert.Equal(["pdf", "docx"], ExtensionFilter.Parse("pdf;docx"));
        Assert.Equal(["pdf"], ExtensionFilter.Parse("pdf, PDF, .Pdf"));
    }

    [Fact]
    public void EmptyAndInvalidTokensAreSkipped()
    {
        Assert.Empty(ExtensionFilter.Parse(""));
        Assert.Empty(ExtensionFilter.Parse("   "));
        Assert.Empty(ExtensionFilter.Parse(null));
        Assert.False(ExtensionFilter.HasAny(""));
        Assert.Equal(["pdf"], ExtensionFilter.Parse("pdf, ***, /etc, tar.gz, ., *"));
        Assert.Equal(["pdf"], ExtensionFilter.Parse("pdf, foo/bar, a\\b, a:b"));
    }

    [Fact]
    public void OfNameUsesLastDotSegment()
    {
        Assert.Equal("pdf", ExtensionFilter.OfName("a.pdf"));
        Assert.Equal("gz", ExtensionFilter.OfName("archive.tar.gz"));
        Assert.Equal("gitignore", ExtensionFilter.OfName(".gitignore"));
        Assert.Equal("", ExtensionFilter.OfName("README"));
        Assert.Equal("", ExtensionFilter.OfName("file."));
        Assert.Equal("", ExtensionFilter.OfName(""));
    }
}
