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
        Assert.False(LocalPathGuard.TryGetFullPath(@"\\server", out _));
        Assert.False(LocalPathGuard.TryGetFullPath(@"\\", out _));
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

    [Fact]
    public void IsSameOrUnderTreatsTrailingSeparatorsAsTheSameRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ifs-slash-" + Guid.NewGuid().ToString("N"));
        var child = Path.Combine(root, "child");
        Directory.CreateDirectory(child);
        try
        {
            var slashed = root + Path.DirectorySeparatorChar;
            Assert.True(LocalPathGuard.IsSameOrUnder(root, slashed));
            Assert.True(LocalPathGuard.IsSameOrUnder(slashed, root));
            Assert.True(LocalPathGuard.IsSameOrUnder(child, slashed));
            Assert.True(LocalPathGuard.TryResolveExistingDirectory(slashed, out var resolved));
            Assert.Equal(Path.GetFullPath(root), resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IsSameOrUnderAcceptsVolumeRootAsScanRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ifs-vol-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var volume = Path.GetPathRoot(root);
            Assert.False(string.IsNullOrEmpty(volume));
            Assert.True(LocalPathGuard.IsSameOrUnder(root, volume!));
            Assert.True(LocalPathGuard.IsSameOrUnder(volume!, volume!));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StripExtendedPrefixMapsToUnprefixedPath()
    {
        Assert.Equal(@"C:\Windows", LocalPathGuard.StripExtendedPrefix(@"\\?\C:\Windows"));
        Assert.Equal(@"C:\Windows", LocalPathGuard.StripExtendedPrefix(@"\\.\C:\Windows"));
        Assert.Equal(@"\\server\share\folder", LocalPathGuard.StripExtendedPrefix(@"\\?\UNC\server\share\folder"));
        Assert.Equal("/tmp/foo", LocalPathGuard.StripExtendedPrefix("/tmp/foo"));
        Assert.Equal("/tmp/foo", LocalPathGuard.CanonicalizeFullPath("/tmp/foo/"));
    }

    [Fact]
    public void WindowsDriveLetterMeansVolumeRootNotCwd()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var drive = Path.GetPathRoot(Path.GetTempPath())?.TrimEnd('\\', '/');
        Assert.False(string.IsNullOrEmpty(drive));
        Assert.Equal(2, drive!.Length);
        Assert.Equal(':', drive[1]);

        Assert.True(LocalPathGuard.TryGetFullPath(drive, out var fromLetter));
        Assert.True(LocalPathGuard.TryGetFullPath(drive + "\\", out var fromRoot));
        Assert.Equal(fromRoot, fromLetter, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(Path.GetFullPath(drive + "\\"), fromLetter, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void WindowsExtendedPrefixResolvesToTheSameDirectory()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "ifs-ext-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var extended = @"\\?\" + Path.GetFullPath(root);
            Assert.True(LocalPathGuard.TryResolveExistingDirectory(extended, out var resolved));
            Assert.False(resolved.StartsWith(@"\\?\", StringComparison.Ordinal));
            Assert.Equal(Path.GetFullPath(root), resolved);
            Assert.True(LocalPathGuard.IsSameOrUnder(extended, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
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
