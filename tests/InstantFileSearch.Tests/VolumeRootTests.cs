using InstantFileSearch;

namespace InstantFileSearch.Tests;

public class VolumeRootTests
{
    [Theory]
    [InlineData(@"C:\Incoming\compressed", @"C:\Incoming\Anchor Span", true)]
    [InlineData(@"C:\data\a", @"c:\data\b", true)]
    [InlineData(@"D:\data\a", @"C:\data\a", false)]
    [InlineData(@"C:\a", @"\\server\share\a", false)]
    [InlineData(@"\\server\share\EngA\compressed", @"\\server\share\EngA\Anchor Span", true)]
    [InlineData(@"\\server\share\a", @"\\server\other\a", false)]
    [InlineData(@"\\server\share\a", @"\\other\share\a", false)]
    [InlineData(@"\\10.1.2.3\c$\Incoming\a", @"\\10.1.2.3\c$\Incoming\b", true)]
    [InlineData(@"\\10.1.2.3\c$\a", @"\\10.1.2.3\d$\a", false)]
    public void SameVolumeIsDriveLetterOrUncShareRoot(string first, string second, bool same)
    {
        Assert.Equal(same, VolumeRoot.AreSame(first, second));
        Assert.True(VolumeRoot.TryGet(first, out var left));
        Assert.True(VolumeRoot.TryGet(second, out var right));
        Assert.Equal(same, left.Equals(right, LocalPathGuard.Comparison));
    }

    [Fact]
    public void DriveVolumeIsTheLetterRoot()
    {
        Assert.True(VolumeRoot.TryGet(@"C:\Record Copy\Incoming\EngA data drive", out var root));
        Assert.Equal(@"C:\", root, StringComparer.OrdinalIgnoreCase);
        Assert.True(VolumeRoot.TryGet(@"d:\foo", out var d));
        Assert.Equal(@"D:\", d, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void UncVolumeIsTheShareRoot()
    {
        Assert.True(VolumeRoot.TryGet(@"\\fileserver\EngA\Incoming\compressed", out var root));
        Assert.Equal(@"\\fileserver\EngA", root, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(@"\\fileserver\EngA", VolumeRoot.ShareRoot(@"\\fileserver\EngA\Incoming\a.zip"));
    }

    [Fact]
    public void UnknownOrMixedPathsAreNotTheSameVolume()
    {
        Assert.False(VolumeRoot.AreSame(null, @"C:\a"));
        Assert.False(VolumeRoot.AreSame("", @"C:\a"));
        Assert.False(VolumeRoot.AreSame(@"C:\a", @"not-a-path"));
    }
}

public class IndexedPathTests
{
    [Fact]
    public void CanonicalizeDoesNotUseGetFullPathForWindowsRoots()
    {
        Assert.True(IndexedPath.TryCanonicalize(@"C:\Incoming\Foo\a.txt", out var path));
        Assert.Equal(@"C:\Incoming\Foo\a.txt", path, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.CurrentDirectory, path, StringComparison.Ordinal);
        Assert.True(IndexedPath.TryGetRelative(@"C:\Incoming\compressed\Foo\a.txt", @"C:\Incoming\compressed", out var relative));
        Assert.Equal(@"Foo\a.txt", relative, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(@"C:\Incoming\Anchor Span\Foo\a.txt", IndexedPath.Combine(@"C:\Incoming\Anchor Span", relative), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void UncRelativePreservesSecondLevelFolder()
    {
        Assert.True(IndexedPath.TryGetRelative(
            @"\\server\share\EngA\compressed\Foo\a.txt",
            @"\\server\share\EngA\compressed",
            out var relative));
        Assert.Equal(@"Foo\a.txt", relative, StringComparer.OrdinalIgnoreCase);
        Assert.True(IndexedPath.IsSameOrUnder(@"\\server\share\EngA\compressed\Foo", @"\\server\share\EngA\compressed"));
        Assert.False(IndexedPath.IsSameOrUnder(@"\\server\other\EngA\compressed\Foo", @"\\server\share\EngA\compressed"));
    }
}
