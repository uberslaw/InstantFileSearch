using System.Text.Json;
using System.Text.Json.Serialization;

namespace InstantFileSearch;

public static class ScanCache
{
    public const int CurrentVersion = 3;
    /// <summary>Oldest snapshots of the same canonical path are dropped first.</summary>
    public const int DefaultMaxSnapshotsPerPath = 10;
    /// <summary>Oldest snapshots across every path are dropped after the per-path cap.</summary>
    public const int DefaultMaxTotalSnapshots = 40;

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

    /// <summary>
    /// Session tree: the new snapshot replaces every loaded root of the same
    /// canonical path. History uses <see cref="AppendHistory"/> instead — a
    /// rescan must not destroy the previous snapshot.
    /// </summary>
    public static IReadOnlyList<ScanResult> Upsert(IReadOnlyList<ScanResult>? existing, ScanResult incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        var list = existing?.ToList() ?? [];
        list.RemoveAll(scan => SameRoot(scan, incoming));
        list.Add(EnsureId(incoming));
        return list;
    }

    /// <summary>
    /// History store: keep the previous snapshot. Caps drop oldest first
    /// (per canonical path, then total). The incoming snapshot is kept.
    /// </summary>
    public static IReadOnlyList<ScanResult> AppendHistory(
        IReadOnlyList<ScanResult>? existing,
        ScanResult incoming,
        int maxPerPath = DefaultMaxSnapshotsPerPath,
        int maxTotal = DefaultMaxTotalSnapshots)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        if (maxPerPath < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPerPath), maxPerPath, "Per-path cap must be at least 1.");
        }

        if (maxTotal < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTotal), maxTotal, "Total cap must be at least 1.");
        }

        var snapshot = EnsureId(incoming);
        var list = existing?.Select(EnsureId).ToList() ?? [];
        if (snapshot.Id != Guid.Empty)
        {
            list.RemoveAll(scan => scan.Id == snapshot.Id);
        }

        list.Add(snapshot);

        var drop = new HashSet<ScanResult>(ReferenceEqualityComparer.Instance);
        foreach (var group in list
                     .Select((scan, index) => (scan, index))
                     .GroupBy(item => CanonicalKey(item.scan)))
        {
            var overflow = group.Count() - maxPerPath;
            if (overflow <= 0)
            {
                continue;
            }

            foreach (var item in group.OrderBy(x => x.scan.CompletedUtc).ThenBy(x => x.index).Take(overflow))
            {
                if (item.scan.Id != snapshot.Id)
                {
                    drop.Add(item.scan);
                }
            }
        }

        var kept = list.Where(scan => !drop.Contains(scan)).ToList();
        if (kept.Count > maxTotal)
        {
            var extra = kept.Count - maxTotal;
            foreach (var item in kept
                         .Select((scan, index) => (scan, index))
                         .OrderBy(x => x.scan.CompletedUtc)
                         .ThenBy(x => x.index))
            {
                if (extra <= 0)
                {
                    break;
                }

                if (item.scan.Id == snapshot.Id)
                {
                    continue;
                }

                kept.Remove(item.scan);
                extra--;
            }
        }

        return kept;
    }

    public static IReadOnlyList<ScanResult> NewestPerPath(IReadOnlyList<ScanResult>? existing)
    {
        if (existing is null || existing.Count == 0)
        {
            return [];
        }

        return existing
            .GroupBy(CanonicalKey)
            .Select(group => group
                .OrderByDescending(scan => scan.CompletedUtc)
                .ThenByDescending(scan => scan.Id)
                .First())
            .OrderBy(scan => scan.CompletedUtc)
            .ToList();
    }

    public static ScanResult? FindById(IReadOnlyList<ScanResult>? existing, Guid id)
    {
        if (existing is null || id == Guid.Empty)
        {
            return null;
        }

        return existing.FirstOrDefault(scan => scan.Id == id);
    }

    /// <summary>
    /// Drops one snapshot from history by id. Does not touch files on disk.
    /// </summary>
    public static IReadOnlyList<ScanResult> WithoutSnapshot(IReadOnlyList<ScanResult>? existing, Guid id)
    {
        var list = existing?.ToList() ?? [];
        if (id == Guid.Empty)
        {
            return list;
        }

        list.RemoveAll(scan => scan.Id == id);
        return list;
    }

    /// <summary>
    /// Drops one loaded scan-root from the session list by tree-node identity.
    /// Two snapshots of the same path can be loaded; only this root is unloaded.
    /// Nested folders and FILES are ignored. Does not touch disk files or history.
    /// </summary>
    public static IReadOnlyList<ScanResult> WithoutRoot(IReadOnlyList<ScanResult>? existing, FolderNode? node)
    {
        var list = existing?.ToList() ?? [];
        if (!FolderFilesNode.IsScanRoot(node))
        {
            return list;
        }

        list.RemoveAll(scan => ReferenceEquals(scan.Root, node));
        return list;
    }

    public static IReadOnlyList<ScanHistoryEntry> ListEntries(
        IReadOnlyList<ScanResult>? existing,
        string? pathFilter = null)
    {
        var filter = pathFilter?.Trim() ?? "";
        var entries = new List<ScanHistoryEntry>();
        foreach (var scan in existing ?? [])
        {
            var entry = ScanHistoryEntry.From(scan);
            if (filter.Length > 0
                && entry.Path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                && entry.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            entries.Add(entry);
        }

        entries.Sort((left, right) =>
        {
            var time = right.CompletedUtc.CompareTo(left.CompletedUtc);
            return time != 0 ? time : string.Compare(left.Path, right.Path, LocalPathGuard.Comparison);
        });
        return entries;
    }

    public static ScanResult EnsureId(ScanResult scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        if (scan.Id != Guid.Empty)
        {
            return scan;
        }

        return new ScanResult
        {
            Id = Guid.NewGuid(),
            Root = scan.Root,
            AllFiles = scan.AllFiles,
            Duration = scan.Duration,
            ErrorCount = scan.ErrorCount,
            CompletedUtc = scan.CompletedUtc,
        };
    }

    public static string CanonicalKey(ScanResult scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        return CanonicalKey(scan.Root.FullPath);
    }

    public static string CanonicalKey(string? fullPath)
    {
        if (LocalPathGuard.TryGetFullPath(fullPath, out var path))
        {
            return path;
        }

        return (fullPath ?? "").Trim();
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<ScanResult>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public bool Equals(ScanResult? x, ScanResult? y) => ReferenceEquals(x, y);

        public int GetHashCode(ScanResult obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
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

    public const string LoadFailedStatus =
        "Could not load saved scans (last-scan.bin). The file may be corrupt or not readable by this Windows user.";

    public static string FormatLoadFailed(string filePath)
    {
        var name = string.IsNullOrWhiteSpace(filePath) ? "last-scan.bin" : Path.GetFileName(filePath);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "last-scan.bin";
        }

        return $"Could not load saved scans ({name}). The file may be corrupt or not readable by this Windows user.";
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

    /// <summary>
    /// True only when at least one scan was parsed. Missing and corrupt files
    /// both return false — use <see cref="LoadAll"/> when the UI must say so.
    /// </summary>
    public static bool TryLoadAll(string filePath, out IReadOnlyList<ScanResult> results, IByteProtector? protector = null)
    {
        var loaded = LoadAll(filePath, protector);
        results = loaded.Scans;
        return loaded.HasScans;
    }

    public static ScanCacheLoad LoadAll(string filePath, IByteProtector? protector = null)
    {
        protector ??= ByteProtector.CreateDefault();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return ScanCacheLoad.Missing();
        }

        var primaryExists = File.Exists(filePath);
        if (ProtectedFile.TryReadAll(filePath, protector, out var bytes))
        {
            // A readable primary file wins, including an empty scan list after
            // the last root was removed. Do not overlay a stale sibling .json.
            if (TryParse(bytes, out var parsed))
            {
                return ScanCacheLoad.Loaded(parsed);
            }

            return ScanCacheLoad.Failed(FormatLoadFailed(filePath));
        }

        var legacyJson = Path.ChangeExtension(filePath, ".json");
        var legacyExists = !filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            && File.Exists(legacyJson);
        if (legacyExists
            && ProtectedFile.TryReadAll(legacyJson, protector, out bytes)
            && TryParse(bytes, out var legacy))
        {
            return ScanCacheLoad.Loaded(legacy);
        }

        if (primaryExists || legacyExists)
        {
            return ScanCacheLoad.Failed(FormatLoadFailed(primaryExists ? filePath : legacyJson));
        }

        return ScanCacheLoad.Missing();
    }

    private static bool SameRoot(ScanResult left, ScanResult right) =>
        SameFolder(left.Root, right.Root);

    private static bool SameFolder(FolderNode left, FolderNode? right) =>
        right is not null
        && (ReferenceEquals(left, right)
            || (LocalPathGuard.TryGetFullPath(left.FullPath, out var a)
                && LocalPathGuard.TryGetFullPath(right.FullPath, out var b)
                && a.Equals(b, LocalPathGuard.Comparison)));

    private static ScanRecord ToRecord(ScanResult result)
    {
        var snapshot = EnsureId(result);
        return new ScanRecord
        {
            Id = snapshot.Id,
            CompletedUtc = snapshot.CompletedUtc == default ? DateTime.UtcNow : snapshot.CompletedUtc,
            DurationSeconds = snapshot.Duration.TotalSeconds,
            ErrorCount = snapshot.ErrorCount,
            Root = FromFolder(snapshot.Root),
        };
    }

    private static bool TryParse(byte[] bytes, out IReadOnlyList<ScanResult> results)
    {
        results = [];
        try
        {
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            var document = JsonSerializer.Deserialize<ScanCacheDocument>(json, Json);
            if (document is null || document.Version is not (1 or 2 or 3))
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

                var completed = record.CompletedUtc == default ? DateTime.UtcNow : record.CompletedUtc;
                if (root.ScanCompletedUtc == default)
                {
                    root.ScanCompletedUtc = completed;
                }

                parsed.Add(new ScanResult
                {
                    Id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id,
                    Root = root,
                    AllFiles = files,
                    Duration = TimeSpan.FromSeconds(Math.Max(0, record.DurationSeconds)),
                    ErrorCount = Math.Max(0, record.ErrorCount),
                    CompletedUtc = completed,
                });
            }

            results = parsed;
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
        public Guid Id { get; set; }
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
