using InstantFileSearch;

namespace InstantFileSearch.Tests;

public class ScanCacheTests
{
    [Fact]
    public void RoundtripRestoresTreeParentsFilesAndCompletedTime()
    {
        var root = Path.Combine(Path.GetTempPath(), "ifs-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "huge"));
        File.WriteAllBytes(Path.Combine(root, "huge", "video.bin"), new byte[4096]);
        File.WriteAllBytes(Path.Combine(root, "readme.txt"), new byte[128]);
        var cache = Path.Combine(Path.GetTempPath(), "ifs-last-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            var original = new FileScanner().Scan(root);
            ScanCache.Save(original, cache);

            Assert.True(ScanCache.TryLoad(cache, out var loaded));
            Assert.NotNull(loaded);
            Assert.Equal(original.Root.FullPath, loaded!.Root.FullPath);
            Assert.Equal(original.Root.Size, loaded.Root.Size);
            Assert.Equal(original.AllFiles.Count, loaded.AllFiles.Count);
            Assert.Equal(original.ErrorCount, loaded.ErrorCount);
            Assert.Equal(original.CompletedUtc, loaded.CompletedUtc, TimeSpan.FromSeconds(1));
            Assert.Equal("huge", loaded.Root.Folders[0].Name);
            Assert.Same(loaded.Root, loaded.Root.Folders[0].Parent);
            Assert.Same(loaded.Root, loaded.AllFiles.Single(f => f.Name == "readme.txt").Parent);
            Assert.Single(FileNameSearch.Filter(loaded.AllFiles, "*.txt"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            if (File.Exists(cache))
            {
                File.Delete(cache);
            }
        }
    }

    [Fact]
    public void SavesAndLoadsMultipleDistinctLocations()
    {
        var one = Path.Combine(Path.GetTempPath(), "ifs-m1-" + Guid.NewGuid().ToString("N"));
        var two = Path.Combine(Path.GetTempPath(), "ifs-m2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(one);
        Directory.CreateDirectory(two);
        File.WriteAllText(Path.Combine(one, "a.txt"), "a");
        File.WriteAllText(Path.Combine(two, "b.txt"), "b");
        var cache = Path.Combine(Path.GetTempPath(), "ifs-multi-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            var first = new FileScanner().Scan(one);
            var second = new FileScanner().Scan(two);
            var combined = ScanCache.Upsert(ScanCache.Upsert([], first), second);
            ScanCache.SaveAll(combined, cache, new PassThroughByteProtector());
            Assert.True(ScanCache.TryLoadAll(cache, out var loaded, new PassThroughByteProtector()));
            Assert.Equal(2, loaded.Count);
            Assert.Contains(loaded, scan => scan.Root.FullPath == first.Root.FullPath);
            Assert.Contains(loaded, scan => scan.Root.FullPath == second.Root.FullPath);

            var again = new FileScanner().Scan(one);
            var replaced = ScanCache.Upsert(loaded, again);
            Assert.Equal(2, replaced.Count);
            Assert.Equal(1, replaced.Count(scan =>
                LocalPathGuard.TryGetFullPath(scan.Root.FullPath, out var path)
                && path.Equals(first.Root.FullPath, LocalPathGuard.Comparison)));
        }
        finally
        {
            Directory.Delete(one, recursive: true);
            Directory.Delete(two, recursive: true);
            if (File.Exists(cache))
            {
                File.Delete(cache);
            }
        }
    }

    [Fact]
    public void TryLoadMissingOrCorruptReturnsFalse()
    {
        Assert.False(ScanCache.TryLoad(Path.Combine(Path.GetTempPath(), "ifs-nope-" + Guid.NewGuid().ToString("N") + ".json"), out _));

        var bad = Path.Combine(Path.GetTempPath(), "ifs-bad-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(bad, "{ not json");
        try
        {
            Assert.False(ScanCache.TryLoad(bad, out _));
        }
        finally
        {
            File.Delete(bad);
        }
    }

    [Fact]
    public void TryLoadRejectsWrongVersion()
    {
        var path = Path.Combine(Path.GetTempPath(), "ifs-ver-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, """{"Version":99,"Root":{"Name":"x","FullPath":"/tmp/x"}}""");
        try
        {
            Assert.False(ScanCache.TryLoad(path, out _));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
