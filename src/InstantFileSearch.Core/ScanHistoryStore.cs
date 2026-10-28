namespace InstantFileSearch;

/// <summary>
/// In-memory history of scan snapshots. The session tree is a separate list;
/// this store is what last-scan.bin persists. Tests inject an instance with
/// smaller caps; the GUI uses the documented defaults.
/// </summary>
public sealed class ScanHistoryStore
{
    private readonly List<ScanResult> _snapshots = [];
    private readonly int _maxPerPath;
    private readonly int _maxTotal;

    public ScanHistoryStore(
        int maxPerPath = ScanCache.DefaultMaxSnapshotsPerPath,
        int maxTotal = ScanCache.DefaultMaxTotalSnapshots)
    {
        if (maxPerPath < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPerPath), maxPerPath, "Per-path cap must be at least 1.");
        }

        if (maxTotal < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTotal), maxTotal, "Total cap must be at least 1.");
        }

        _maxPerPath = maxPerPath;
        _maxTotal = maxTotal;
    }

    public int MaxSnapshotsPerPath => _maxPerPath;

    public int MaxTotalSnapshots => _maxTotal;

    public IReadOnlyList<ScanResult> Snapshots => _snapshots;

    public int Count => _snapshots.Count;

    public void ReplaceAll(IReadOnlyList<ScanResult>? snapshots)
    {
        _snapshots.Clear();
        if (snapshots is null || snapshots.Count == 0)
        {
            return;
        }

        foreach (var scan in snapshots)
        {
            _snapshots.Add(ScanCache.EnsureId(scan));
        }
    }

    public ScanResult Append(ScanResult incoming)
    {
        var next = ScanCache.AppendHistory(_snapshots, incoming, _maxPerPath, _maxTotal);
        _snapshots.Clear();
        _snapshots.AddRange(next);
        var id = incoming.Id;
        if (id != Guid.Empty)
        {
            return _snapshots.First(scan => scan.Id == id);
        }

        return _snapshots.OrderByDescending(scan => scan.CompletedUtc).First();
    }

    public bool Delete(Guid id)
    {
        if (id == Guid.Empty)
        {
            return false;
        }

        var next = ScanCache.WithoutSnapshot(_snapshots, id);
        if (next.Count == _snapshots.Count)
        {
            return false;
        }

        _snapshots.Clear();
        _snapshots.AddRange(next);
        return true;
    }

    public ScanResult? Find(Guid id) => ScanCache.FindById(_snapshots, id);

    public IReadOnlyList<ScanResult> NewestPerPath() => ScanCache.NewestPerPath(_snapshots);

    public IReadOnlyList<ScanHistoryEntry> List(string? pathFilter = null) =>
        ScanCache.ListEntries(_snapshots, pathFilter);

    public void ReplaceSnapshot(ScanResult updated)
    {
        ArgumentNullException.ThrowIfNull(updated);
        if (updated.Id == Guid.Empty)
        {
            return;
        }

        var index = _snapshots.FindIndex(scan => scan.Id == updated.Id);
        if (index >= 0)
        {
            _snapshots[index] = updated;
        }
    }
}
