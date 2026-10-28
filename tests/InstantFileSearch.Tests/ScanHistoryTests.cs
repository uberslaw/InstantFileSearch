using InstantFileSearch;

namespace InstantFileSearch.Tests;

public class ScanHistoryTests
{
    [Fact]
    public void SamePathScannedTwiceKeepsTwoHistoryEntriesAndRestoresNewest()
    {
        var root = MakeDir("hist-twice");
        var cache = TempCache("hist-twice");
        var protector = new PassThroughByteProtector();
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "a");
            var first = At(new FileScanner().Scan(root), Utc(2026, 1, 1, 12, 0));
            File.WriteAllText(Path.Combine(root, "b.txt"), "b");
            var second = At(new FileScanner().Scan(root), Utc(2026, 2, 2, 15, 30));

            var history = ScanCache.AppendHistory(ScanCache.AppendHistory([], first), second);
            Assert.Equal(2, history.Count);
            ScanCache.SaveAll(history, cache, protector);

            Assert.True(ScanCache.TryLoadAll(cache, out var loaded, protector));
            Assert.Equal(2, loaded.Count);
            var newest = ScanCache.NewestPerPath(loaded);
            Assert.Single(newest);
            Assert.Equal(second.CompletedUtc, newest[0].CompletedUtc, TimeSpan.FromSeconds(1));
            Assert.Contains("b.txt", newest[0].AllFiles.Select(file => file.Name));
            Assert.Equal(2, loaded.Count(scan =>
                ScanCache.CanonicalKey(scan) == ScanCache.CanonicalKey(first)));
        }
        finally
        {
            Cleanup(root, cache);
        }
    }

    [Fact]
    public void DeleteHistoryEntryIsGoneLoadPutsOldSnapshotWithDateInName()
    {
            var older = Snapshot("/tmp/work", Utc(2026, 3, 1, 8, 0), Entry("gone.txt", "/tmp/work/gone.txt"));
        var newer = Snapshot("/tmp/work", Utc(2026, 4, 1, 9, 0), Entry("stay.txt", "/tmp/work/stay.txt"));
        var store = new ScanHistoryStore();
        store.Append(older);
        store.Append(newer);

        var loaded = store.Find(older.Id);
        Assert.NotNull(loaded);
        Assert.Contains(ScanRefreshPrompt.FormatDoneAt(older.CompletedUtc), loaded!.Root.TreeLabel);
        Assert.Equal(older.Root.Name + "  ·  " + ScanRefreshPrompt.FormatDoneAt(older.CompletedUtc), loaded.Root.TreeLabel);

        Assert.True(store.Delete(older.Id));
        Assert.Null(store.Find(older.Id));
        Assert.Single(store.Snapshots);
        Assert.Equal(newer.Id, store.Snapshots[0].Id);
        Assert.False(store.Delete(older.Id));
    }

    [Fact]
    public void CapDropsOldestPerPathThenTotal()
    {
        var store = new ScanHistoryStore(maxPerPath: 2, maxTotal: 3);
        var pathA = "/tmp/hist-a";
        var pathB = "/tmp/hist-b";
        var a1 = Snapshot(pathA, Utc(2026, 1, 1, 1, 0), Entry("a1.txt", pathA + "/a1.txt"));
        var a2 = Snapshot(pathA, Utc(2026, 1, 2, 1, 0), Entry("a2.txt", pathA + "/a2.txt"));
        var a3 = Snapshot(pathA, Utc(2026, 1, 3, 1, 0), Entry("a3.txt", pathA + "/a3.txt"));
        store.Append(a1);
        store.Append(a2);
        store.Append(a3);
        Assert.Equal(2, store.Count);
        Assert.Null(store.Find(a1.Id));
        Assert.NotNull(store.Find(a2.Id));
        Assert.NotNull(store.Find(a3.Id));

        var b1 = Snapshot(pathB, Utc(2026, 1, 4, 1, 0), Entry("b1.txt", pathB + "/b1.txt"));
        var b2 = Snapshot(pathB, Utc(2026, 1, 5, 1, 0), Entry("b2.txt", pathB + "/b2.txt"));
        store.Append(b1);
        store.Append(b2);
        Assert.Equal(3, store.Count);
        Assert.Null(store.Find(a2.Id));
        Assert.Contains(store.Snapshots, scan => scan.Id == a3.Id);
        Assert.Contains(store.Snapshots, scan => scan.Id == b1.Id);
        Assert.Contains(store.Snapshots, scan => scan.Id == b2.Id);
        Assert.Equal(2, store.MaxSnapshotsPerPath);
        Assert.Equal(3, store.MaxTotalSnapshots);
        Assert.Equal(10, ScanCache.DefaultMaxSnapshotsPerPath);
        Assert.Equal(40, ScanCache.DefaultMaxTotalSnapshots);
        Assert.Equal(SearchScopeKind.AllPrevious, SearchScopes.Parse(SearchScopes.AllPrevious));
        Assert.Equal(SearchScopes.AllPrevious, ResultsUi.ScopeAllPrevious);
    }

    [Fact]
    public void AllPreviousSearchFileOnlyInOlderSnapshotStillHitsWithThatDate()
    {
        var olderTime = Utc(2026, 5, 1, 10, 0);
        var newerTime = Utc(2026, 6, 1, 10, 0);
        var older = Snapshot("/tmp/hist-search", olderTime, Entry("gone.txt", "/tmp/hist-search/gone.txt"));
        var newer = Snapshot("/tmp/hist-search", newerTime, Entry("stay.txt", "/tmp/hist-search/stay.txt"));

        var hits = ScanHistorySearch.Search([older, newer], new SearchQuery { Text = "gone.txt" });
        var hit = Assert.Single(hits);
        Assert.False(hit.IsFolder);
        Assert.Equal("/tmp/hist-search/gone.txt", hit.FullPath);
        Assert.Equal(olderTime, hit.LastPresentUtc);
        Assert.Equal(olderTime, ScanHistorySearch.LastPresentUtc([older, newer], hit.FullPath));
    }

    [Fact]
    public void AllPreviousSearchFileInTwoSnapshotsIsOneRowWithNewerLastPresent()
    {
        var olderTime = Utc(2026, 5, 1, 10, 0);
        var newerTime = Utc(2026, 6, 1, 10, 0);
        var older = Snapshot("/tmp/hist-both", olderTime, Entry("keep.txt", "/tmp/hist-both/keep.txt"));
        var newer = Snapshot("/tmp/hist-both", newerTime, Entry("keep.txt", "/tmp/hist-both/keep.txt"));

        var hits = ScanHistorySearch.Search([older, newer], new SearchQuery { Text = "keep.txt" });
        var hit = Assert.Single(hits);
        Assert.Equal(newerTime, hit.LastPresentUtc);
    }

    [Fact]
    public void AllPreviousSearchFoldersUseTheSameLastPresentRuleAndSkipFilesNodes()
    {
        var olderTime = Utc(2026, 7, 1, 10, 0);
        var newerTime = Utc(2026, 8, 1, 10, 0);
        var olderRoot = new FolderNode { Name = "work", FullPath = "/tmp/hist-fold", ScanCompletedUtc = olderTime };
        var nested = new FolderNode { Name = "archive", FullPath = "/tmp/hist-fold/archive", Parent = olderRoot };
        olderRoot.Folders.Add(nested);
        FolderFilesNode.Attach(olderRoot);
        var older = new ScanResult
        {
            Id = Guid.NewGuid(),
            Root = olderRoot,
            AllFiles = [],
            CompletedUtc = olderTime,
        };

        var newerRoot = new FolderNode { Name = "work", FullPath = "/tmp/hist-fold", ScanCompletedUtc = newerTime };
        FolderFilesNode.Attach(newerRoot);
        var newer = new ScanResult
        {
            Id = Guid.NewGuid(),
            Root = newerRoot,
            AllFiles = [],
            CompletedUtc = newerTime,
        };

        var hits = ScanHistorySearch.Search([older, newer], new SearchQuery { Text = "archive" });
        var hit = Assert.Single(hits);
        Assert.True(hit.IsFolder);
        Assert.Equal("/tmp/hist-fold/archive", hit.FullPath);
        Assert.Equal(olderTime, hit.LastPresentUtc);
        Assert.DoesNotContain(hits, item => item.Folder?.IsFilesNode == true);

        var workHits = ScanHistorySearch.Search([older, newer], new SearchQuery { Text = "work" });
        var work = Assert.Single(workHits);
        Assert.True(work.IsFolder);
        Assert.Equal(newerTime, work.LastPresentUtc);
    }

    [Fact]
    public void WithoutRootUnloadsOnlyThatSnapshotNotTheSamePathSibling()
    {
        var older = Snapshot("/tmp/hist-unload", Utc(2026, 1, 1, 1, 0), Entry("a.txt", "/tmp/hist-unload/a.txt"));
        var newer = Snapshot("/tmp/hist-unload", Utc(2026, 2, 1, 1, 0), Entry("b.txt", "/tmp/hist-unload/b.txt"));
        var session = new[] { older, newer };
        var remaining = ScanCache.WithoutRoot(session, older.Root);
        Assert.Single(remaining);
        Assert.Same(newer, remaining[0]);
        Assert.Equal(2, ScanCache.WithoutRoot(session, older.Root.Folders.FirstOrDefault()).Count);
    }

    [Fact]
    public void ListEntriesFiltersByPathSubstringNewestFirst()
    {
        var one = Snapshot("/tmp/alpha", Utc(2026, 1, 1, 1, 0));
        var two = Snapshot("/tmp/beta", Utc(2026, 2, 1, 1, 0));
        var entries = ScanCache.ListEntries([one, two], "beta");
        var entry = Assert.Single(entries);
        Assert.Equal(two.Id, entry.Id);
        Assert.Equal(ScanLocation.KindLabel(two.Root.LocationKind), entry.LocationText);
        Assert.Equal(ScanRefreshPrompt.FormatDoneAt(two.CompletedUtc), entry.CompletedLocalText);

        var all = ScanCache.ListEntries([one, two]);
        Assert.Equal(two.Id, all[0].Id);
        Assert.Equal(one.Id, all[1].Id);
    }

    [Fact]
    public void V2CacheMigratesAndAppendKeepsThePreviousSnapshot()
    {
        var cache = Path.Combine(Path.GetTempPath(), "ifs-v2-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(cache, """
            {"Version":2,"Scans":[{"CompletedUtc":"2026-01-02T03:04:05Z","DurationSeconds":1.5,"ErrorCount":0,"Root":{"Name":"/tmp/v2root","FullPath":"/tmp/v2root","Size":0,"FileCount":0,"FolderCount":0,"Modified":"2026-01-02T03:04:05","Folders":[],"Files":[]}}]}
            """);
        try
        {
            Assert.True(ScanCache.TryLoadAll(cache, out var loaded, new PassThroughByteProtector()));
            var first = Assert.Single(loaded);
            Assert.Equal("/tmp/v2root", first.Root.FullPath);
            Assert.NotEqual(Guid.Empty, first.Id);

            var newer = Snapshot("/tmp/v2root", Utc(2026, 9, 1, 12, 0), Entry("later.txt", "/tmp/v2root/later.txt"));
            var history = ScanCache.AppendHistory(loaded, newer);
            Assert.Equal(2, history.Count);
            ScanCache.SaveAll(history, cache, new PassThroughByteProtector());
            Assert.True(ScanCache.TryLoadAll(cache, out var roundtrip, new PassThroughByteProtector()));
            Assert.Equal(2, roundtrip.Count);
            Assert.All(roundtrip, scan => Assert.NotEqual(Guid.Empty, scan.Id));
        }
        finally
        {
            if (File.Exists(cache))
            {
                File.Delete(cache);
            }
        }
    }

    [Fact]
    public void SessionUpsertStillReplacesSamePathInTheTree()
    {
        var older = Snapshot("/tmp/hist-session", Utc(2026, 1, 1, 1, 0));
        var newer = Snapshot("/tmp/hist-session", Utc(2026, 2, 1, 1, 0));
        var session = ScanCache.Upsert([older], newer);
        Assert.Single(session);
        Assert.Equal(newer.CompletedUtc, session[0].CompletedUtc);
        Assert.Equal(2, ScanCache.AppendHistory([older], newer).Count);
    }

    [Fact]
    public void TreeLabelLeavesNestedFoldersUnchanged()
    {
        var root = new FolderNode
        {
            Name = "work (this PC)",
            FullPath = "/tmp/work",
            ScanCompletedUtc = Utc(2026, 10, 1, 14, 0),
        };
        var child = new FolderNode { Name = "src", FullPath = "/tmp/work/src", Parent = root };
        Assert.Contains(ScanRefreshPrompt.FormatDoneAt(root.ScanCompletedUtc), root.TreeLabel);
        Assert.Equal("src", child.TreeLabel);
        Assert.Equal("FILES", new FolderNode
        {
            Name = FolderFilesNode.DisplayName,
            FullPath = "/tmp/work",
            Parent = root,
            IsFilesNode = true,
        }.TreeLabel);
    }

    private static ScanResult Snapshot(string path, DateTime completed, params FileEntry[] files)
    {
        var root = new FolderNode
        {
            Name = path,
            FullPath = path,
            ScanCompletedUtc = completed,
            LocationKind = ScanLocationKind.Local,
            FileCount = files.Length,
        };
        foreach (var file in files)
        {
            root.Files.Add(file);
        }

        FolderFilesNode.Attach(root);
        return new ScanResult
        {
            Id = Guid.NewGuid(),
            Root = root,
            AllFiles = files,
            CompletedUtc = completed,
        };
    }

    private static FileEntry Entry(string name, string path) => new()
    {
        Name = name,
        FullPath = path,
        Size = 1,
        Modified = DateTime.UnixEpoch,
    };

    private static ScanResult At(ScanResult scan, DateTime completed)
    {
        scan.Root.ScanCompletedUtc = completed;
        return new ScanResult
        {
            Id = scan.Id == Guid.Empty ? Guid.NewGuid() : scan.Id,
            Root = scan.Root,
            AllFiles = scan.AllFiles,
            Duration = scan.Duration,
            ErrorCount = scan.ErrorCount,
            CompletedUtc = completed,
        };
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static string MakeDir(string tag)
    {
        var root = Path.Combine(Path.GetTempPath(), "ifs-" + tag + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string TempCache(string tag) =>
        Path.Combine(Path.GetTempPath(), "ifs-" + tag + "-" + Guid.NewGuid().ToString("N") + ".bin");

    private static void Cleanup(string root, string cache)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        if (File.Exists(cache))
        {
            File.Delete(cache);
        }
    }
}
