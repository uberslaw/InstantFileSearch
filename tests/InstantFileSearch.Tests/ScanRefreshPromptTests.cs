using InstantFileSearch;

namespace InstantFileSearch.Tests;

public class ScanRefreshPromptTests
{
    [Fact]
    public void LoadedFromCacheRootShouldPromptJustScannedShouldNot()
    {
        var cached = Scan("cached");
        var fresh = Scan("fresh");
        var session = new ScanRefreshSession();
        session.MarkLoadedFromCache(cached);
        session.MarkFreshThisSession(fresh);

        Assert.True(session.ShouldAsk(cached.Root));
        Assert.False(session.ShouldAsk(fresh.Root));
        Assert.False(session.ShouldAsk(null));
    }

    [Fact]
    public void AfterNoDoesNotPromptAgain()
    {
        var cached = Scan("declined");
        var session = new ScanRefreshSession();
        session.MarkLoadedFromCache(cached);

        var asked = 0;
        var refresh = ScanRefreshPrompt.TryAsk(session, cached.Root, cached.CompletedUtc, _ =>
        {
            asked++;
            return false;
        });

        Assert.False(refresh);
        Assert.Equal(1, asked);
        Assert.False(session.ShouldAsk(cached.Root));
        Assert.False(ScanRefreshPrompt.TryAsk(session, cached.Root, cached.CompletedUtc, _ => true));
    }

    [Fact]
    public void NestedFolderAndFilesDoNotPrompt()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "ifs-prompt-nest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(rootPath, "sub"));
        File.WriteAllText(Path.Combine(rootPath, "a.txt"), "a");
        File.WriteAllText(Path.Combine(rootPath, "sub", "b.txt"), "b");
        try
        {
            var scan = new FileScanner().Scan(rootPath);
            FolderFilesNode.Attach(scan.Root);
            var session = new ScanRefreshSession();
            session.MarkLoadedFromCache(scan);

            var nested = scan.Root.Folders.Single(node => node.Name == "sub");
            var files = scan.Root.TreeChildren.Single(node => node.IsFilesNode);

            Assert.True(session.ShouldAsk(scan.Root));
            Assert.False(session.ShouldAsk(nested));
            Assert.False(session.ShouldAsk(files));
            Assert.False(FolderFilesNode.IsScanRoot(nested));
            Assert.False(FolderFilesNode.IsScanRoot(files));

            var asked = 0;
            Assert.False(ScanRefreshPrompt.TryAsk(session, nested, scan.CompletedUtc, _ => { asked++; return true; }));
            Assert.False(ScanRefreshPrompt.TryAsk(session, files, scan.CompletedUtc, _ => { asked++; return true; }));
            Assert.Equal(0, asked);
            Assert.True(session.ShouldAsk(scan.Root));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public void YesMarksRootFreshThisSession()
    {
        var cached = Scan("refresh-yes");
        var session = new ScanRefreshSession();
        session.MarkLoadedFromCache(cached);

        var refresh = ScanRefreshPrompt.TryAsk(session, cached.Root, cached.CompletedUtc, _ => true);
        Assert.True(refresh);
        Assert.False(session.ShouldAsk(cached.Root));
    }

    [Fact]
    public void MessageUsesScanTimestampOnTheRoot()
    {
        var completed = new DateTime(2026, 3, 15, 18, 30, 0, DateTimeKind.Utc);
        var root = new FolderNode
        {
            Name = "work (this PC)",
            FullPath = Path.Combine(Path.GetTempPath(), "ifs-prompt-time"),
            ScanCompletedUtc = completed,
        };
        var expected = ScanRefreshPrompt.FormatDoneAt(completed);
        Assert.Equal(completed.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), expected);

        var message = ScanRefreshPrompt.Message(root.FullPath, root.ScanCompletedUtc);
        Assert.Contains(root.FullPath, message, StringComparison.Ordinal);
        Assert.Contains(expected, message, StringComparison.Ordinal);
        Assert.Contains("Refresh now?", message, StringComparison.Ordinal);
        Assert.StartsWith($"This scan of {root.FullPath} was done at {expected}.", message, StringComparison.Ordinal);

        Assert.Equal("Scanned " + expected, new FolderNode
        {
            Name = root.Name,
            FullPath = root.FullPath,
            ScanCompletedUtc = completed,
        }.ScanTimeTip);
        Assert.Equal("", new FolderNode
        {
            Name = "sub",
            FullPath = Path.Combine(root.FullPath, "sub"),
            Parent = root,
            ScanCompletedUtc = completed,
        }.ScanTimeTip);
    }

    private static ScanResult Scan(string label)
    {
        var root = Path.Combine(Path.GetTempPath(), "ifs-prompt-" + label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "n.txt"), label);
        try
        {
            return new FileScanner().Scan(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
