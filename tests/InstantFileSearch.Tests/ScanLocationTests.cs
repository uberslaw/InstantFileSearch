using InstantFileSearch;

namespace InstantFileSearch.Tests;

public class ScanLocationTests
{
    [Fact]
    public void UncPathsAreNetwork()
    {
        Assert.Equal(ScanLocationKind.Network, ScanLocation.Classify(@"\\fileserver\share\docs"));
        Assert.Equal(ScanLocationKind.Network, ScanLocation.Classify(@"\\10.33.41.9\c$"));
        Assert.Equal(ScanLocationKind.Network, ScanLocation.Classify(@"\\SERVER\c$\Windows"));
        Assert.Equal("network", ScanLocation.KindLabel(ScanLocationKind.Network));
        Assert.Contains("(network)", ScanLocation.DisplayName(@"\\fileserver\share\docs", ScanLocationKind.Network), StringComparison.Ordinal);
        Assert.Equal("network", ScanLocation.KindLabel(ScanLocationKind.Network));
        Assert.Contains("(network)", ScanLocation.DisplayName(@"\\fileserver\share\docs", ScanLocationKind.Network), StringComparison.Ordinal);
    }

    [Fact]
    public void TempFolderIsLocal()
    {
        var path = Path.GetTempPath();
        Assert.Equal(ScanLocationKind.Local, ScanLocation.Classify(path));
        Assert.Contains("(this PC)", ScanLocation.DisplayName(path, ScanLocationKind.Local), StringComparison.Ordinal);
    }

    [Fact]
    public void ScanLogWritesDurationLocationAndPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "ifs-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "a.txt"), "x");
        var log = Path.Combine(Path.GetTempPath(), "ifs-scans-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            var result = new FileScanner().Scan(root);
            ScanLog.Append(result, log);
            var text = File.ReadAllText(log);
            Assert.Contains(result.Root.FullPath, text, StringComparison.Ordinal);
            Assert.Contains("this PC", text, StringComparison.Ordinal);
            Assert.Contains("files", text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            if (File.Exists(log))
            {
                File.Delete(log);
            }
        }
    }
}
