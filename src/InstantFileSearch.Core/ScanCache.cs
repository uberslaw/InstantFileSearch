using System.Text.Json;
using System.Text.Json.Serialization;

namespace InstantFileSearch;

public static class ScanCache
{
    public const int CurrentVersion = 2;

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

    public static IReadOnlyList<ScanResult> Upsert(IReadOnlyList<ScanResult>? existing, ScanResult incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        var list = existing?.ToList() ?? [];
        var index = list.FindIndex(scan => SameRoot(scan, incoming));
        if (index >= 0)
        {
            list[index] = incoming;
        }
        else
        {
            list.Add(incoming);
        }

        return list;
    }

    public static void Save(ScanResult result, string filePath, IByteProtector? protector = null) =>
        SaveAll([result], filePath, protector);

    public static void SaveAll(IReadOnlyList<ScanResult> results, string filePath, IByteProtector? protector = null)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A cache path is required.", nameof(filePath));
        }

        protector ??= ByteProtector.CreateDefault();
        var document = new ScanCacheDocument
        {
            Version = CurrentVersion,
            Scans = results.Select(ToRecord).ToList(),
        };
        var json = JsonSerializer.Serialize(document, Json);
        ProtectedFile.WriteAll(filePath, System.Text.Encoding.UTF8.GetBytes(json), protector);
    }

    public static bool TryLoad(string filePath, out ScanResult? result, IByteProtector? protector = null)
    {
        result = null;
        if (!TryLoadAll(filePath, out var all, protector) || all.Count == 0)
        {
            return false;
        }

        result = all[0];
        return true;
    }

    public static bool TryLoadAll(string filePath, out IReadOnlyList<ScanResult> results, IByteProtector? protector = null)
    {
        results = [];
        protector ??= ByteProtector.CreateDefault();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        byte[] bytes;
        if (ProtectedFile.TryReadAll(filePath, protector, out bytes)
            && TryParse(bytes, out var parsed))
        {
            results = parsed;
            return parsed.Count > 0;
        }

        var legacyJson = Path.ChangeExtension(filePath, ".json");
        if (!filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            && File.Exists(legacyJson)
            && ProtectedFile.TryReadAll(legacyJson, protector, out bytes)
            && TryParse(bytes, out parsed))
        {
            results = parsed;
            return parsed.Count > 0;
        }

        return false;
    }

    private static bool SameRoot(ScanResult left, ScanResult right) =>
        LocalPathGuard.TryGetFullPath(left.Root.FullPath, out var a)
        && LocalPathGuard.TryGetFullPath(right.Root.FullPath, out var b)
        && a.Equals(b, LocalPathGuard.Comparison);

    private static ScanRecord ToRecord(ScanResult result) => new()
    {
        CompletedUtc = result.CompletedUtc == default ? DateTime.UtcNow : result.CompletedUtc,
        DurationSeconds = result.Duration.TotalSeconds,
        ErrorCount = result.ErrorCount,
        Root = FromFolder(result.Root),
    };

    private static bool TryParse(byte[] bytes, out IReadOnlyList<ScanResult> results)
    {
        results = [];
        try
        {
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            var document = JsonSerializer.Deserialize<ScanCacheDocument>(json, Json);
            if (document is null || document.Version is not (1 or 2))
            {
                return false;
            }

            var records = new List<ScanRecord>();
            if (document.Scans is { Count: > 0 })
            {
                records.AddRange(document.Scans);
            }
            else if (document.Root is not null)
            {
                records.Add(new ScanRecord
                {
                    CompletedUtc = document.CompletedUtc,
                    DurationSeconds = document.DurationSeconds,
                    ErrorCount = document.ErrorCount,
                    Root = document.Root,
                });
            }

            var parsed = new List<ScanResult>();
            foreach (var record in records)
            {
                if (record.Root is null
                    || string.IsNullOrWhiteSpace(record.Root.Name)
                    || string.IsNullOrWhiteSpace(record.Root.FullPath))
                {
                    continue;
                }

                var files = new List<FileEntry>();
                var root = ToFolder(record.Root, parent: null, files);
                FolderFilesNode.Attach(root);
                if (root.ScanDuration <= TimeSpan.Zero && record.DurationSeconds > 0)
                {
                    root.ScanDuration = TimeSpan.FromSeconds(record.DurationSeconds);
                }

                parsed.Add(new ScanResult
                {
                    Root = root,
                    AllFiles = files,
                    Duration = TimeSpan.FromSeconds(Math.Max(0, record.DurationSeconds)),
                    ErrorCount = Math.Max(0, record.ErrorCount),
                    CompletedUtc = record.CompletedUtc == default ? DateTime.UtcNow : record.CompletedUtc,
                });
            }

            results = parsed;
            return parsed.Count > 0;
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
        LocationKind = node.LocationKind.ToString(),
        ScanDurationSeconds = node.ScanDuration.TotalSeconds,
        Folders = node.Folders.Where(child => !child.IsFilesNode).Select(FromFolder).ToList(),
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
        var kind = Enum.TryParse<ScanLocationKind>(record.LocationKind, ignoreCase: true, out var parsed)
            ? parsed
            : ScanLocationKind.Unknown;
        if (parent is null && kind == ScanLocationKind.Unknown)
        {
            kind = ScanLocation.Classify(record.FullPath);
        }

        var name = record.Name;
        if (parent is null && (string.IsNullOrWhiteSpace(name) || name == record.FullPath))
        {
            name = ScanLocation.DisplayName(record.FullPath, kind);
        }

        var node = new FolderNode
        {
            Name = name,
            FullPath = record.FullPath,
            Parent = parent,
            Size = record.Size,
            FileCount = record.FileCount,
            FolderCount = record.FolderCount,
            Modified = record.Modified,
            LocationKind = parent is null ? kind : ScanLocationKind.Unknown,
            ScanDuration = parent is null ? TimeSpan.FromSeconds(Math.Max(0, record.ScanDurationSeconds)) : TimeSpan.Zero,
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
        public List<ScanRecord>? Scans { get; set; }
    }

    private sealed class ScanRecord
    {
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
        public string LocationKind { get; set; } = "";
        public double ScanDurationSeconds { get; set; }
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
