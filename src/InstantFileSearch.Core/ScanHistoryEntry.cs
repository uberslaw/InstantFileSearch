namespace InstantFileSearch;

/// <summary>
/// One row in Scan history: path, this-PC vs network, completed local time,
/// duration, file count, size. Load uses <see cref="Id"/>; Delete removes this
/// snapshot from the cache only.
/// </summary>
public sealed class ScanHistoryEntry
{
    public required Guid Id { get; init; }
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required string TreeLabel { get; init; }
    public ScanLocationKind LocationKind { get; init; }
    public required string LocationText { get; init; }
    public DateTime CompletedUtc { get; init; }
    public required string CompletedLocalText { get; init; }
    public TimeSpan Duration { get; init; }
    public required string DurationText { get; init; }
    public int FileCount { get; init; }
    public long Size { get; init; }
    public required string SizeText { get; init; }

    public static ScanHistoryEntry From(ScanResult scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        var snapshot = ScanCache.EnsureId(scan);
        var root = snapshot.Root;
        var completed = snapshot.CompletedUtc == default ? root.ScanCompletedUtc : snapshot.CompletedUtc;
        return new ScanHistoryEntry
        {
            Id = snapshot.Id,
            Path = root.FullPath,
            Name = root.Name,
            TreeLabel = root.TreeLabel,
            LocationKind = root.LocationKind,
            LocationText = ScanLocation.KindLabel(root.LocationKind),
            CompletedUtc = completed,
            CompletedLocalText = ScanRefreshPrompt.FormatDoneAt(completed),
            Duration = snapshot.Duration,
            DurationText = ScanLocation.FormatDuration(snapshot.Duration),
            FileCount = root.FileCount,
            Size = root.Size,
            SizeText = ByteFormatter.ToString(root.Size),
        };
    }
}
