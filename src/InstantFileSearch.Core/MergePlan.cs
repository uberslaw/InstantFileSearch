namespace InstantFileSearch;

public sealed class MergePlan
{
    public required FolderNode SourceFolder { get; init; }
    public required FolderNode DestFolder { get; init; }
    public required ScanResult SourceScan { get; init; }
    public required ScanResult DestScan { get; init; }
    public required IReadOnlyList<MergeItem> Items { get; init; }
    public CollisionPolicy Policy { get; init; }
    public bool SameVolume { get; init; }
    public int CollisionCount { get; init; }
    public int TransferCount { get; init; }
    public long CopyBytes { get; init; }

    public int FileCount => Items.Count;

    public bool SameScan => ReferenceEquals(SourceScan, DestScan);
}

public sealed class MergeItem
{
    public required FileEntry File { get; init; }
    public required string SourcePath { get; init; }
    public required string DestPath { get; init; }
    public required string RelativePath { get; init; }
    public bool IndexCollision { get; init; }
    public bool WillTransfer { get; init; }
}

public sealed class MergePlanResult
{
    public MergePlan? Plan { get; init; }
    public string? Error { get; init; }
    public bool DestNeedsScan { get; init; }

    public static MergePlanResult Ok(MergePlan plan) => new() { Plan = plan };

    public static MergePlanResult Fail(string error, bool destNeedsScan = false) =>
        new() { Error = error, DestNeedsScan = destNeedsScan };
}

public sealed class MergeProgress
{
    public int Moved { get; init; }
    public int Skipped { get; init; }
    public int Failed { get; init; }
    public int Total { get; init; }
    public string CurrentPath { get; init; } = "";

    public string StatusText => IndexedFileMerge.ProgressText(Moved, Total, Skipped, Failed);
}

public sealed class MergeExecuteResult
{
    public int Moved { get; init; }
    public int Skipped { get; init; }
    public int Failed { get; init; }
    public int Total { get; init; }
    public bool Cancelled { get; init; }
    public required ScanResult SourceScan { get; init; }
    public required ScanResult DestScan { get; init; }

    public string StatusText => IndexedFileMerge.ProgressText(Moved, Total, Skipped, Failed);
}
