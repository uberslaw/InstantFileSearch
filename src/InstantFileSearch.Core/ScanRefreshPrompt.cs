namespace InstantFileSearch;

/// <summary>
/// "Refresh this cached scan?" is a Core decision so tests can inject confirm
/// without a WPF MessageBox. Only scan roots loaded from cache prompt; nested
/// folders, FILES, and scans started this session do not.
/// </summary>
public static class ScanRefreshPrompt
{
    public const string Title = "Refresh scan?";

    public static string FormatDoneAt(DateTime completedUtc)
    {
        var when = completedUtc == default ? DateTime.UtcNow : completedUtc;
        if (when.Kind == DateTimeKind.Unspecified)
        {
            when = DateTime.SpecifyKind(when, DateTimeKind.Utc);
        }

        return when.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    public static string Message(string path, DateTime completedUtc)
    {
        var where = string.IsNullOrWhiteSpace(path) ? "this folder" : path.Trim();
        return $"This scan of {where} was done at {FormatDoneAt(completedUtc)}.{Environment.NewLine}{Environment.NewLine}Refresh now?";
    }

    /// <summary>
    /// Asks once when <paramref name="session"/> says this root is a stale cache
    /// snapshot. Yes = caller should rescan (same as Scan on that path). No =
    /// keep browsing the snapshot; do not ask again this session.
    /// </summary>
    public static bool TryAsk(
        ScanRefreshSession session,
        FolderNode? node,
        DateTime completedUtc,
        Func<string, bool> confirm)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(confirm);
        if (!session.ShouldAsk(node) || node is null)
        {
            return false;
        }

        if (confirm(Message(node.FullPath, completedUtc)))
        {
            session.MarkFreshThisSession(node);
            return true;
        }

        session.RememberDeclined(node);
        return false;
    }
}

/// <summary>
/// Per-process memory of which scan roots came from last-scan.bin versus were
/// scanned (or refreshed) in this session.
/// </summary>
public sealed class ScanRefreshSession
{
    private readonly HashSet<string> _stale = new(PathComparer);
    private readonly HashSet<string> _settled = new(PathComparer);

    private static readonly StringComparer PathComparer =
        StringComparer.FromComparison(LocalPathGuard.Comparison);

    public void MarkLoadedFromCache(ScanResult scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        _stale.Add(Key(scan.Root));
        _settled.Remove(Key(scan.Root));
    }

    public void MarkFreshThisSession(FolderNode? node)
    {
        if (node is null)
        {
            return;
        }

        var key = Key(node);
        _stale.Remove(key);
        _settled.Add(key);
    }

    public void MarkFreshThisSession(ScanResult scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        MarkFreshThisSession(scan.Root);
    }

    public void RememberDeclined(FolderNode? node)
    {
        if (node is null)
        {
            return;
        }

        _settled.Add(Key(node));
    }

    public bool ShouldAsk(FolderNode? node)
    {
        if (node is null || !FolderFilesNode.IsScanRoot(node))
        {
            return false;
        }

        var key = Key(node);
        return _stale.Contains(key) && !_settled.Contains(key);
    }

    private static string Key(FolderNode node)
    {
        if (LocalPathGuard.TryGetFullPath(node.FullPath, out var full))
        {
            return full;
        }

        return (node.FullPath ?? "").Trim();
    }
}
