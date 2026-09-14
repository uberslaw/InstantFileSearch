using InstantFileSearch;

namespace InstantFileSearch.Tests;

public class ByteFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1.00 KB")]
    [InlineData(1536, "1.50 KB")]
    [InlineData(1048576, "1.00 MB")]
    [InlineData(1073741824, "1.00 GB")]
    public void FormatsExpectedUnits(long bytes, string expected)
    {
        Assert.Equal(expected, ByteFormatter.ToString(bytes));
    }
}

public class FileNameSearchTests
{
    [Fact]
    public void MatchesSubstringAndWildcard()
    {
        var files = new[]
        {
            new FileEntry { Name = "Report.xlsx", FullPath = @"C:\Work\Report.xlsx", Size = 10, Modified = DateTime.UnixEpoch },
            new FileEntry { Name = "notes.txt", FullPath = @"C:\Work\notes.txt", Size = 4, Modified = DateTime.UnixEpoch },
        };

        Assert.Single(FileNameSearch.Filter(files, "report"));
        Assert.Single(FileNameSearch.Filter(files, "*.txt"));
        Assert.Empty(FileNameSearch.Filter(files, "missing"));
    }
}

public class FileScannerTests
{
    [Fact]
    public void ScanBuildsTreeSizesAndSearchIndex()
    {
        var root = Path.Combine(Path.GetTempPath(), "ifs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "huge"));
        Directory.CreateDirectory(Path.Combine(root, "tiny"));
        File.WriteAllBytes(Path.Combine(root, "huge", "video.bin"), new byte[4096]);
        File.WriteAllBytes(Path.Combine(root, "tiny", "readme.txt"), new byte[128]);
        File.WriteAllBytes(Path.Combine(root, "root.log"), new byte[32]);

        try
        {
            var result = new FileScanner().Scan(root);

            Assert.Equal(4096 + 128 + 32, result.Root.Size);
            Assert.Equal(3, result.Root.FileCount);
            Assert.Equal(2, result.Root.FolderCount);
            Assert.Equal("huge", result.Root.Folders[0].Name);
            Assert.Equal(4096, result.Root.Folders[0].Size);
            Assert.Equal(3, result.AllFiles.Count);
            Assert.Single(FileNameSearch.Filter(result.AllFiles, "*.txt"));
            Assert.Single(FileNameSearch.Filter(result.AllFiles, "video"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ScanMissingFolderThrows()
    {
        Assert.Throws<DirectoryNotFoundException>(() =>
            new FileScanner().Scan(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N"))));
    }
}
