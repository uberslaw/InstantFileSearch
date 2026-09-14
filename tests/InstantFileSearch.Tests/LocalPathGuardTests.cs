using InstantFileSearch;

namespace InstantFileSearch.Tests;

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

            File.WriteAllText(Path.Combine(root, "keep", "new-after-scan.txt"), "y");
            Assert.False(LocalPathGuard.TryValidateOpenPath(Path.Combine(root, "keep", "new-after-scan.txt"), result, out _));

            File.Delete(indexedFile);
            Assert.False(LocalPathGuard.TryValidateOpenPath(indexedFile, result, out _));

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

public class FolderNodeTests
{
    [Fact]
    public void ExpandAncestorsExpandsParentsNotSelf()
    {
        var root = new FolderNode { Name = "root", FullPath = "/tmp/root" };
        var child = new FolderNode { Name = "child", FullPath = "/tmp/root/child", Parent = root };
        var leaf = new FolderNode { Name = "leaf", FullPath = "/tmp/root/child/leaf", Parent = child };

        leaf.ExpandAncestors();

        Assert.True(root.IsExpanded);
        Assert.True(child.IsExpanded);
        Assert.False(leaf.IsExpanded);
    }
}
