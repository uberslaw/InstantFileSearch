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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ScanEmptyPathThrows(string? path)
    {
        Assert.Throws<ArgumentException>(() => new FileScanner().Scan(path!));
    }
}

public class LocalPathGuardTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsEmptyDirectory(string? path)
    {
        Assert.False(LocalPathGuard.TryResolveExistingDirectory(path, out var fullPath));
        Assert.Equal("", fullPath);
    }

    [Fact]
    public void RejectsMissingDirectory()
    {
        var missing = Path.Combine(Path.GetTempPath(), "ifs-missing-" + Guid.NewGuid().ToString("N"));
        Assert.False(LocalPathGuard.TryResolveExistingDirectory(missing, out _));
    }

    [Fact]
    public void RejectsFileAsScanDirectory()
    {
        var file = Path.GetTempFileName();
        try
        {
            Assert.False(LocalPathGuard.TryResolveExistingDirectory(file, out _));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void RejectsInvalidPath()
    {
        Assert.False(LocalPathGuard.TryGetFullPath("\0", out _));
        Assert.False(LocalPathGuard.TryResolveExistingDirectory("\0", out _));
        Assert.Throws<ArgumentException>(() => new FileScanner().Scan("\0"));
    }

    [Fact]
    public void AcceptsExistingDirectoryAndResolvesDotSegments()
    {
        var root = Path.Combine(Path.GetTempPath(), "ifs-ok-" + Guid.NewGuid().ToString("N"));
        var sub = Path.Combine(root, "sub");
        Directory.CreateDirectory(sub);
        try
        {
            var dotted = Path.Combine(root, ".", "sub");
            Assert.True(LocalPathGuard.TryResolveExistingDirectory(dotted, out var fullPath));
            Assert.Equal(Path.GetFullPath(sub), fullPath);

            var viaParent = Path.Combine(sub, "..", "sub");
            Assert.True(LocalPathGuard.TryResolveExistingDirectory(viaParent, out var viaParentFull));
            Assert.Equal(Path.GetFullPath(sub), viaParentFull);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IsSameOrUnderRejectsSiblingPrefix()
    {
        var root = Path.Combine(Path.GetTempPath(), "ifs-pre-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.True(LocalPathGuard.IsSameOrUnder(root, root));
            Assert.True(LocalPathGuard.IsSameOrUnder(Path.Combine(root, "child"), root));
            Assert.False(LocalPathGuard.IsSameOrUnder(root + "-sibling", root));
            Assert.False(LocalPathGuard.IsSameOrUnder(Path.GetTempPath(), root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void OpenPathMustExistAndComeFromTheIndex()
    {
        var root = Path.Combine(Path.GetTempPath(), "ifs-idx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "keep"));
        File.WriteAllText(Path.Combine(root, "keep", "a.txt"), "x");

        var outside = Path.Combine(Path.GetTempPath(), "ifs-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "no");

        try
        {
            var result = new FileScanner().Scan(root);
            var indexedFile = Path.Combine(root, "keep", "a.txt");
            var indexedFolder = Path.Combine(root, "keep");

            Assert.True(LocalPathGuard.TryValidateOpenPath(indexedFile, result, out var filePath));
            Assert.Equal(Path.GetFullPath(indexedFile), filePath);
            Assert.True(LocalPathGuard.TryValidateOpenPath(indexedFolder, result, out _));
            Assert.True(LocalPathGuard.TryValidateOpenPath(result.Root.FullPath, result, out _));

            var stillInside = Path.Combine(root, "keep", "..", "keep", "a.txt");
            Assert.True(LocalPathGuard.TryValidateOpenPath(stillInside, result, out _));

            Assert.False(LocalPathGuard.TryValidateOpenPath(Path.Combine(outside, "secret.txt"), result, out _));
            Assert.False(LocalPathGuard.TryValidateOpenPath(
                Path.Combine(root, "keep", "..", "..", Path.GetFileName(outside), "secret.txt"),
                result,
                out _));
            Assert.False(LocalPathGuard.TryValidateOpenPath(Path.Combine(root, "keep", "gone.txt"), result, out _));
            Assert.False(LocalPathGuard.TryValidateOpenPath(indexedFile, scan: null, out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }
}
