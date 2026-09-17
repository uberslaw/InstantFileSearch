using InstantFileSearch;

namespace InstantFileSearch.Tests;

public class UncPathTests
{
    [Theory]
    [InlineData(@"\\10.33.41.9\c$")]
    [InlineData(@"\\10.33.41.9\c$\Windows")]
    [InlineData(@"\\SERVER\c$")]
    [InlineData(@"\\fileserver\share\docs")]
    [InlineData(@"//fileserver/share/docs")]
    [InlineData(@"\\?\UNC\fileserver\share\docs")]
    [InlineData(@"\\[::1]\c$")]
    [InlineData(@"\\[2001:db8::1]\share")]
    public void AcceptsHostIpAndAdminShareSyntax(string path)
    {
        Assert.True(UncPath.TryNormalize(path, out var normalized));
        Assert.StartsWith(@"\\", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.CurrentDirectory, normalized, StringComparison.Ordinal);
        Assert.True(LocalPathGuard.TryGetFullPath(path, out var full));
        Assert.Equal(normalized, full);
        Assert.Equal(ScanLocationKind.Network, ScanLocation.Classify(path));
        if (!OperatingSystem.IsWindows())
        {
            Assert.False(LocalPathGuard.TryResolveExistingDirectory(path, out _));
        }
    }

    [Theory]
    [InlineData(@"\\10.33.41.9\c$")]
    [InlineData(@"\\SERVER\C$")]
    [InlineData(@"\\host\d$\Users")]
    [InlineData(@"\\host\ADMIN$")]
    public void DetectsAdminShares(string path)
    {
        Assert.True(UncPath.IsAdminShare(path));
    }

    [Fact]
    public void OrdinaryShareIsNotAdminShare()
    {
        Assert.False(UncPath.IsAdminShare(@"\\fileserver\share\docs"));
        Assert.False(UncPath.IsAdminShare(@"C:\Windows"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"\\")]
    [InlineData(@"\\server")]
    [InlineData(@"\\server\")]
    [InlineData(@"\\\share")]
    [InlineData(@"\\server\share\..\..")]
    [InlineData(@"\\server\share\foo\..\..")]
    [InlineData(@"\\2001:db8::1\share")]
    [InlineData("\0")]
    public void RejectsIncompleteOrEscapingUnc(string? path)
    {
        Assert.False(UncPath.TryNormalize(path, out var normalized));
        Assert.Equal("", normalized);
        Assert.False(LocalPathGuard.TryGetFullPath(path, out _));
    }

    [Fact]
    public void ResolvesDotSegmentsInsideTheShare()
    {
        Assert.True(UncPath.TryNormalize(@"\\server\share\foo\..\bar", out var path));
        Assert.Equal(@"\\server\share\bar", path);
        Assert.True(UncPath.TryNormalize(@"\\server\share\.\docs", out var dotted));
        Assert.Equal(@"\\server\share\docs", dotted);
    }

    [Fact]
    public void IsSameOrUnderUsesUncSeparatorsOnLinux()
    {
        Assert.True(LocalPathGuard.IsSameOrUnder(@"\\server\share\docs\a.txt", @"\\server\share"));
        Assert.True(LocalPathGuard.IsSameOrUnder(@"\\server\share", @"\\server\share"));
        Assert.False(LocalPathGuard.IsSameOrUnder(@"\\server\other\a.txt", @"\\server\share"));
        Assert.False(LocalPathGuard.IsSameOrUnder(@"\\server\share-extra\a.txt", @"\\server\share"));
    }

    [Fact]
    public void ExtendedLocalPrefixIsNotNetwork()
    {
        Assert.NotEqual(ScanLocationKind.Network, ScanLocation.Classify(@"\\?\C:\Windows"));
        Assert.Equal(ScanLocationKind.Network, ScanLocation.Classify(@"\\?\UNC\fileserver\share"));
    }
}

public class ScanAccessTests
{
    [Fact]
    public void MissingAdminShareSuggestsElevation()
    {
        var text = ScanAccess.Missing(@"\\10.33.41.9\c$");
        Assert.Contains("not found or not accessible", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("administrator", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"\\10.33.41.9\c$", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeniedAdminSharePointsAtRunAsAdministratorWhenNotElevated()
    {
        var unelevated = ScanAccess.Denied(@"\\SERVER\c$", elevated: false);
        Assert.Contains("Run as administrator", unelevated, StringComparison.Ordinal);
        Assert.Contains(@"\\SERVER\c$", unelevated, StringComparison.OrdinalIgnoreCase);

        var elevated = ScanAccess.Denied(@"\\SERVER\c$", elevated: true);
        Assert.DoesNotContain("Run as administrator", elevated, StringComparison.Ordinal);
        Assert.Contains("Access denied", elevated, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalMissingFolderKeepsFolderNotFoundWording()
    {
        Assert.StartsWith("Folder not found:", ScanAccess.Missing(@"C:\missing-ifs"), StringComparison.Ordinal);
    }
}

public class ProcessElevationTests
{
    [Fact]
    public void LinuxHostIsNotElevatedAndTitlesCompose()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.False(ProcessElevation.IsCurrentProcessElevated());
        }

        Assert.Equal("Instant File Search", ProcessElevation.WindowTitle(elevated: false, editMode: false));
        Assert.Equal("Instant File Search — Administrator", ProcessElevation.WindowTitle(elevated: true, editMode: false));
        Assert.Equal("Instant File Search — Edit", ProcessElevation.WindowTitle(elevated: false, editMode: true));
        Assert.Equal("Instant File Search — Administrator — Edit", ProcessElevation.WindowTitle(elevated: true, editMode: true));
        Assert.Equal("Administrator", ProcessElevation.PrivilegeLabel(true));
        Assert.Equal("", ProcessElevation.PrivilegeLabel(false));
        Assert.Contains("does not prompt", ProcessElevation.RunAsAdministratorTip(false), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Already", ProcessElevation.RunAsAdministratorTip(true), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RelaunchUsesRunasAndDoesNotScan()
    {
        var info = ProcessElevation.RelaunchStartInfo("/tmp/InstantFileSearch.exe");
        Assert.Equal("/tmp/InstantFileSearch.exe", info.FileName);
        Assert.Equal("runas", info.Verb);
        Assert.True(info.UseShellExecute);
    }
}
