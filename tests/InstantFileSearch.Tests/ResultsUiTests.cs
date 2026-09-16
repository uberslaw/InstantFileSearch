using InstantFileSearch;

namespace InstantFileSearch.Tests;

public class ResultsUiTests
{
    [Fact]
    public void EmptyStateDependsOnScanSearchAndCount()
    {
        Assert.True(ResultsUi.ShowEmptyState(0));
        Assert.False(ResultsUi.ShowEmptyState(1));
        Assert.Equal("Scan a folder to list files here.", ResultsUi.EmptyState(hasScans: false, isSearchActive: false, itemCount: 0));
        Assert.Equal("This folder is empty.", ResultsUi.EmptyState(hasScans: true, isSearchActive: false, itemCount: 0));
        Assert.Equal("No files at this level.", ResultsUi.EmptyState(hasScans: true, isSearchActive: false, itemCount: 0, filesAtLevel: true));
        Assert.Equal("No files match this search.", ResultsUi.EmptyState(hasScans: true, isSearchActive: true, itemCount: 0));
        Assert.Equal("", ResultsUi.EmptyState(hasScans: true, isSearchActive: true, itemCount: 3));
    }

    [Fact]
    public void ContentsHeaderShowsResultCountLikeEverything()
    {
        Assert.Equal("CONTENTS", ResultsUi.ContentsHeader(hasScans: false, isSearchActive: false, itemCount: 0));
        Assert.Equal("CONTENTS · 1 item", ResultsUi.ContentsHeader(hasScans: true, isSearchActive: false, itemCount: 1));
        Assert.Equal("CONTENTS · 12 items", ResultsUi.ContentsHeader(hasScans: true, isSearchActive: false, itemCount: 12));
        Assert.Equal("Files in work · 1 file", ResultsUi.ContentsHeader(true, false, 1, filesAtLevel: true, folderName: "work"));
        Assert.Equal("Files in this folder · 3 files", ResultsUi.ContentsHeader(true, false, 3, filesAtLevel: true, folderName: null));
        Assert.Equal("RESULTS · 1 file", ResultsUi.ContentsHeader(hasScans: true, isSearchActive: true, itemCount: 1));
        Assert.Equal("RESULTS · 0 files", ResultsUi.ContentsHeader(hasScans: true, isSearchActive: true, itemCount: 0));
    }

    [Fact]
    public void DetailsMetaOmitsMissingDates()
    {
        Assert.Equal("File  ·  512 B", ResultsUi.DetailsMeta(512, DateTime.MinValue, isFolder: false));
        Assert.Equal("Files  ·  512 B", ResultsUi.DetailsMeta(512, DateTime.MinValue, isFolder: true, isFilesNode: true));
        Assert.Equal("Folder  ·  1.00 KB  ·  2024-02-03 04:05", ResultsUi.DetailsMeta(1024, new DateTime(2024, 2, 3, 4, 5, 0), isFolder: true));
    }

    [Fact]
    public void TreeContextCopyIgnoresGridSelection()
    {
        Assert.True(ResultsUi.IsTreeContext("tree"));
        Assert.True(ResultsUi.IsTreeContext("TREE"));
        Assert.False(ResultsUi.IsTreeContext(null));
        Assert.False(ResultsUi.IsTreeContext("grid"));

        Assert.Equal(@"C:\work", ResultsUi.ContextPath(@"C:\work\a.txt", @"C:\work", treeContext: true));
        Assert.Equal(@"C:\work\a.txt", ResultsUi.ContextPath(@"C:\work\a.txt", @"C:\work", treeContext: false));
        Assert.Equal(@"C:\work", ResultsUi.ContextPath(null, @"C:\work", treeContext: false));
        Assert.Equal("work", ResultsUi.ContextName("a.txt", "work", treeContext: true));
        Assert.Equal("a.txt", ResultsUi.ContextName("a.txt", "work", treeContext: false));
    }

    [Fact]
    public void PercentHidesOnRootsAndFloorsTinyShares()
    {
        Assert.Equal("", ResultsUi.FormatPercent(100, isRoot: true));
        Assert.Equal("0%", ResultsUi.FormatPercent(0, isRoot: false));
        Assert.Equal("0%", ResultsUi.FormatPercent(double.NaN, isRoot: false));
        Assert.Equal("<1%", ResultsUi.FormatPercent(0.4, isRoot: false));
        Assert.Equal("42%", ResultsUi.FormatPercent(42.2, isRoot: false));
        Assert.Equal("100%", ResultsUi.FormatPercent(140, isRoot: false));
    }

    [Fact]
    public void FolderGlyphDistinguishesDriveNetworkAndChildFolder()
    {
        Assert.Equal("\uE7F4", ResultsUi.FolderGlyph(isRoot: true, ScanLocationKind.Local));
        Assert.Equal("\uE83B", ResultsUi.FolderGlyph(isRoot: true, ScanLocationKind.Network));
        Assert.Equal("\uE8B7", ResultsUi.FolderGlyph(isRoot: false, ScanLocationKind.Network));
        Assert.Equal("\uE8C8", ResultsUi.FolderGlyph(isRoot: false, ScanLocationKind.Local, isFilesNode: true));
        Assert.Equal("\uE8C8", ResultsUi.FilesGlyph());
        Assert.NotEqual(ResultsUi.FilesGlyph(), ResultsUi.FolderGlyph(isRoot: false, ScanLocationKind.Local));
        Assert.NotEqual(ResultsUi.FilesGlyph(), ResultsUi.FolderGlyph(isRoot: true, ScanLocationKind.Local));
        Assert.Equal("\uE8B7", ResultsUi.FileGlyph(isFolder: true));
        Assert.Equal("\uE7C3", ResultsUi.FileGlyph(isFolder: false));
    }

    [Fact]
    public void PercentBarWidthClampsAndRejectsNaN()
    {
        Assert.Equal(0, UiLayout.PercentToWidth(50, 0));
        Assert.Equal(0, UiLayout.PercentToWidth(50, double.NaN));
        Assert.Equal(0, UiLayout.PercentToWidth(double.NaN, 100));
        Assert.Equal(40, UiLayout.PercentToWidth(40, 100));
        Assert.Equal(100, UiLayout.PercentToWidth(200, 100));
        Assert.Equal(0, UiLayout.PercentToWidth(-10, 100));
    }

    [Fact]
    public void FolderNodePercentAndGlyphFollowResultsUi()
    {
        var root = new FolderNode { Name = "C", FullPath = @"C:\", LocationKind = ScanLocationKind.Local, Size = 1000 };
        var child = new FolderNode { Name = "work", FullPath = @"C:\work", Parent = root, Size = 4 };
        Assert.Equal("", root.PercentText);
        Assert.Equal("\uE7F4", root.Glyph);
        Assert.Equal("<1%", child.PercentText);
        Assert.Equal("\uE8B7", child.Glyph);
        var files = new FolderNode { Name = "FILES", FullPath = @"C:\work", Parent = root, Size = 4, IsFilesNode = true };
        Assert.Equal(ResultsUi.FilesGlyph(), files.Glyph);
        Assert.Equal("<1%", files.PercentText);
    }
}
