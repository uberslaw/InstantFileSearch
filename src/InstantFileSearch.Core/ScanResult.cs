namespace InstantFileSearch;

public sealed class ScanResult
{
    /// <summary>
    /// Stable id for this snapshot in the history store. Empty until
    /// <see cref="ScanCache.EnsureId"/> or a scanner/cache load assigns one.
    /// </summary>
    public Guid Id { get; init; }

    public required FolderNode Root { get; init; }
    public required IReadOnlyList<FileEntry> AllFiles { get; init; }
    public TimeSpan Duration { get; init; }
    public int ErrorCount { get; init; }
    public DateTime CompletedUtc { get; init; }

    public ScanResult WithFiles(IReadOnlyList<FileEntry> files) => new()
    {
        Id = Id,
        Root = Root,
        AllFiles = files,
        Duration = Duration,
        ErrorCount = ErrorCount,
        CompletedUtc = CompletedUtc,
    };
}
