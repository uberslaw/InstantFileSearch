using InstantFileSearch;
using InstantFileSearch.Cli;

namespace InstantFileSearch.Tests;

public class FolderExclusionTests
{
    [Fact]
    public void ScanSkipsExcludedDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "ifs-ex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "keep"));
        Directory.CreateDirectory(Path.Combine(root, "skip"));
        File.WriteAllBytes(Path.Combine(root, "keep", "ok.bin"), new byte[32]);
        File.WriteAllBytes(Path.Combine(root, "skip", "secret.bin"), new byte[64]);
        try
        {
            var skip = Path.Combine(root, "skip");
            var result = new FileScanner().Scan(root, excludeDirectories: [skip]);
            Assert.Contains(result.AllFiles, f => f.Name == "ok.bin");
            Assert.DoesNotContain(result.AllFiles, f => f.Name == "secret.bin");
            Assert.DoesNotContain(result.Root.Folders, f => f.Name == "skip");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

public class ProtectedCacheTests
{
    [Fact]
    public void SavedCacheIsNotPlainJson()
    {
        var root = Path.Combine(Path.GetTempPath(), "ifs-enc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "a.txt"), "x");
        var cache = Path.Combine(Path.GetTempPath(), "ifs-bin-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            var original = new FileScanner().Scan(root);
            ScanCache.Save(original, cache, new PassThroughByteProtector());
            var raw = File.ReadAllBytes(cache);
            Assert.Equal(ProtectedFile.Magic, raw.Take(ProtectedFile.Magic.Length).ToArray());
            Assert.NotEqual((byte)'{', raw[0]);
            Assert.True(ScanCache.TryLoad(cache, out var loaded, new PassThroughByteProtector()));
            Assert.Equal(original.AllFiles.Count, loaded!.AllFiles.Count);
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
    public void WrongProtectorCannotReadCache()
    {
        var root = Path.Combine(Path.GetTempPath(), "ifs-xor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "a.txt"), "x");
        var cache = Path.Combine(Path.GetTempPath(), "ifs-xor-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            ScanCache.Save(new FileScanner().Scan(root), cache, new FlipProtector());
            Assert.False(ScanCache.TryLoad(cache, out _, new PassThroughByteProtector()));
            Assert.True(ScanCache.TryLoad(cache, out var loaded, new FlipProtector()));
            Assert.NotNull(loaded);
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
    public void LoadsLegacyPlainJsonCache()
    {
        var path = Path.Combine(Path.GetTempPath(), "ifs-legacy-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, """{"Version":1,"CompletedUtc":"2026-01-02T03:04:05Z","DurationSeconds":1.5,"ErrorCount":0,"Root":{"Name":"/tmp/x","FullPath":"/tmp/x","Size":0,"FileCount":0,"FolderCount":0,"Modified":"2026-01-02T03:04:05","Folders":[],"Files":[]}}""");
        try
        {
            Assert.True(ScanCache.TryLoad(path, out var loaded, new PassThroughByteProtector()));
            Assert.Equal("/tmp/x", loaded!.Root.FullPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class FlipProtector : IByteProtector
    {
        public byte[] Protect(byte[] plaintext) => plaintext.Select(b => (byte)~b).ToArray();

        public byte[] Unprotect(byte[] ciphertext) => Protect(ciphertext);
    }
}

public class CliHostTests
{
    [Fact]
    public void ScanSearchStatusAndExcludeRoundTrip()
    {
        var root = Path.Combine(Path.GetTempPath(), "ifs-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "keep"));
        Directory.CreateDirectory(Path.Combine(root, "skip"));
        File.WriteAllBytes(Path.Combine(root, "keep", "notes.txt"), new byte[8]);
        File.WriteAllBytes(Path.Combine(root, "skip", "noise.bin"), new byte[8]);
        var cache = Path.Combine(Path.GetTempPath(), "ifs-cli-cache-" + Guid.NewGuid().ToString("N") + ".bin");
        var exclusions = Path.Combine(Path.GetTempPath(), "ifs-cli-ex-" + Guid.NewGuid().ToString("N") + ".bin");
        var protector = new PassThroughByteProtector();
        try
        {
            var skip = Path.Combine(root, "skip");
            Assert.Equal(0, Run(["exclude", "add", skip], cache, exclusions, protector, out var added, out _));
            Assert.Contains("Excluded", added);
            Assert.Equal(0, Run(["scan", root], cache, exclusions, protector, out var scanOut, out _));
            Assert.Contains("Last scan", scanOut);
            Assert.Equal(0, Run(["search", "*.txt"], cache, exclusions, protector, out var searchOut, out _));
            Assert.Contains("notes.txt", searchOut);
            Assert.Contains("File", searchOut);
            Assert.DoesNotContain("noise.bin", searchOut);
            Assert.Equal(0, Run(["search", "keep"], cache, exclusions, protector, out var folderOut, out _));
            Assert.Contains("Folder", folderOut);
            Assert.Contains(Path.Combine(root, "keep"), folderOut);
            Assert.DoesNotContain("notes.txt", folderOut);
            Assert.Equal(0, Run(["status"], cache, exclusions, protector, out var status, out _));
            Assert.Contains(root, status);
            Assert.Contains("Excluded folders: 1", status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            if (File.Exists(cache))
            {
                File.Delete(cache);
            }

            if (File.Exists(exclusions))
            {
                File.Delete(exclusions);
            }
        }
    }

    private static int Run(string[] args, string cache, string exclusions, IByteProtector protector, out string stdout, out string stderr)
    {
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();
        var code = CliHost.Run(args, outWriter, errWriter, cache, exclusions, protector);
        stdout = outWriter.ToString();
        stderr = errWriter.ToString();
        return code;
    }
}
