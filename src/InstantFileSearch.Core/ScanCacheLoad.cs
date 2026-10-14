namespace InstantFileSearch;

public enum ScanCacheLoadStatus
{
    Missing = 0,
    Loaded = 1,
    Failed = 2,
}

public readonly struct ScanCacheLoad
{
    public ScanCacheLoadStatus Status { get; init; }
    public IReadOnlyList<ScanResult> Scans { get; init; }
    public string? Error { get; init; }

    public bool HasScans => Status == ScanCacheLoadStatus.Loaded && Scans.Count > 0;

    public static ScanCacheLoad Missing() => new()
    {
        Status = ScanCacheLoadStatus.Missing,
        Scans = [],
    };

    public static ScanCacheLoad Loaded(IReadOnlyList<ScanResult> scans) => new()
    {
        Status = ScanCacheLoadStatus.Loaded,
        Scans = scans ?? [],
    };

    public static ScanCacheLoad Failed(string error) => new()
    {
        Status = ScanCacheLoadStatus.Failed,
        Scans = [],
        Error = error,
    };
}
