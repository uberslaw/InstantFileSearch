namespace InstantFileSearch;

public sealed class ScanResult
{
    public required FolderNode Root { get; init; }
    public required IReadOnlyList<FileEntry> AllFiles { get; init; }
    public TimeSpan Duration { get; init; }
    public int ErrorCount { get; init; }
    public DateTime CompletedUtc { get; init; }
}
