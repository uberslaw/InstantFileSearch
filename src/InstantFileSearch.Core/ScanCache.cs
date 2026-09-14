using System.Text.Json;
using System.Text.Json.Serialization;

namespace InstantFileSearch;

public static class ScanCache
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string DefaultFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "InstantFileSearch",
            "last-scan.bin");

    public static void Save(ScanResult result, string filePath, IByteProtector? protector = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A cache path is required.", nameof(filePath));
        }

        protector ??= ByteProtector.CreateDefault();

        var document = new ScanCacheDocument
        {
            Version = CurrentVersion,
            CompletedUtc = result.CompletedUtc == default ? DateTime.UtcNow : result.CompletedUtc,
            DurationSeconds = result.Duration.TotalSeconds,
            ErrorCount = result.ErrorCount,
            Root = FromFolder(result.Root),
        };

        var json = JsonSerializer.Serialize(document, Json);
        ProtectedFile.WriteAll(filePath, System.Text.Encoding.UTF8.GetBytes(json), protector);
    }

    public static bool TryLoad(string filePath, out ScanResult? result, IByteProtector? protector = null)
    {
        result = null;
        protector ??= ByteProtector.CreateDefault();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        byte[] bytes;
        if (ProtectedFile.TryReadAll(filePath, protector, out bytes))
        {
            return TryParse(bytes, out result);
        }

        var legacyJson = Path.ChangeExtension(filePath, ".json");
        if (!filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            && File.Exists(legacyJson)
            && ProtectedFile.TryReadAll(legacyJson, protector, out bytes))
        {
            return TryParse(bytes, out result);
        }

        return false;
    }

    private static bool TryParse(byte[] bytes, out ScanResult? result)
    {
        result = null;
        try
        {
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            var document = JsonSerializer.Deserialize<ScanCacheDocument>(json, Json);
            if (document is null || document.Version != CurrentVersion || document.Root is null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(document.Root.Name) ||
                string.IsNullOrWhiteSpace(document.Root.FullPath))
            {
                return false;
            }

            var files = new List<FileEntry>();
            var root = ToFolder(document.Root, parent: null, files);
            result = new ScanResult
            {
                Root = root,
                AllFiles = files,
                Duration = TimeSpan.FromSeconds(Math.Max(0, document.DurationSeconds)),
                ErrorCount = Math.Max(0, document.ErrorCount),
                CompletedUtc = document.CompletedUtc == default ? DateTime.UtcNow : document.CompletedUtc,
            };
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static FolderRecord FromFolder(FolderNode node) => new()
    {
        Name = node.Name,
        FullPath = node.FullPath,
        Size = node.Size,
        FileCount = node.FileCount,
        FolderCount = node.FolderCount,
        Modified = node.Modified,
        Folders = node.Folders.Select(FromFolder).ToList(),
        Files = node.Files.Select(FromFile).ToList(),
    };

    private static FileRecord FromFile(FileEntry file) => new()
    {
        Name = file.Name,
        FullPath = file.FullPath,
        Size = file.Size,
        Modified = file.Modified,
    };

    private static FolderNode ToFolder(FolderRecord record, FolderNode? parent, List<FileEntry> allFiles)
    {
        var node = new FolderNode
        {
            Name = record.Name,
            FullPath = record.FullPath,
            Parent = parent,
            Size = record.Size,
            FileCount = record.FileCount,
            FolderCount = record.FolderCount,
            Modified = record.Modified,
        };

        foreach (var child in record.Folders)
        {
            node.Folders.Add(ToFolder(child, node, allFiles));
        }

        foreach (var file in record.Files)
        {
            var entry = new FileEntry
            {
                Name = file.Name,
                FullPath = file.FullPath,
                Size = file.Size,
                Modified = file.Modified,
                Parent = node,
            };
            node.Files.Add(entry);
            allFiles.Add(entry);
        }

        return node;
    }

    private sealed class ScanCacheDocument
    {
        public int Version { get; set; }
        public DateTime CompletedUtc { get; set; }
        public double DurationSeconds { get; set; }
        public int ErrorCount { get; set; }
        public FolderRecord? Root { get; set; }
    }

    private sealed class FolderRecord
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public long Size { get; set; }
        public int FileCount { get; set; }
        public int FolderCount { get; set; }
        public DateTime Modified { get; set; }
        public List<FolderRecord> Folders { get; set; } = [];
        public List<FileRecord> Files { get; set; } = [];
    }

    private sealed class FileRecord
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public long Size { get; set; }
        public DateTime Modified { get; set; }
    }
}
