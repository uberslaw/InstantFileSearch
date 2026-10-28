namespace InstantFileSearch;

/// <summary>
/// Search across stored snapshots. Hits are unique by file/folder FullPath
/// (case-insensitive on Windows). Last present is the CompletedUtc of the
/// latest snapshot that contained that path — even when the path is missing
/// from newer snapshots.
/// </summary>
public static class ScanHistorySearch
{
    public static IReadOnlyList<SearchHit> Search(
        IReadOnlyList<ScanResult> snapshots,
        SearchQuery query,
        int limit = FileNameSearch.DefaultLimit)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(query);
        if (limit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be at least 1.");
        }

        if (!query.HasCriteria || snapshots.Count == 0)
        {
            return [];
        }

        var comparer = StringComparer.FromComparison(LocalPathGuard.Comparison);
        var presence = new Dictionary<string, DateTime>(comparer);
        foreach (var snapshot in snapshots)
        {
            IndexPresence(snapshot, presence);
        }

        var hits = new Dictionary<string, SearchHit>(comparer);
        var matchTime = new Dictionary<string, DateTime>(comparer);
        foreach (var snapshot in snapshots)
        {
            var folders = FolderFilesNode.FoldersForSearch(
                [snapshot.Root],
                selected: null,
                selectedFolderScope: false);
            foreach (var hit in FileNameSearch.FilterHits(folders, snapshot.AllFiles, query, limit))
            {
                var key = ScanCache.CanonicalKey(hit.FullPath);
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                var lastPresent = presence.TryGetValue(key, out var when)
                    ? when
                    : snapshot.CompletedUtc;
                var stamped = hit.WithLastPresent(lastPresent);
                var previousMatch = matchTime.TryGetValue(key, out var previous) ? previous : DateTime.MinValue;
                if (!hits.ContainsKey(key) || snapshot.CompletedUtc >= previousMatch)
                {
                    hits[key] = stamped;
                    matchTime[key] = snapshot.CompletedUtc;
                }
            }
        }

        return hits.Values
            .OrderByDescending(hit => hit.LastPresentUtc)
            .ThenBy(hit => hit.FullPath, comparer)
            .Take(limit)
            .ToList();
    }

    public static DateTime LastPresentUtc(IReadOnlyList<ScanResult> snapshots, string? fullPath)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        var key = ScanCache.CanonicalKey(fullPath);
        if (string.IsNullOrEmpty(key))
        {
            return default;
        }

        var latest = default(DateTime);
        foreach (var snapshot in snapshots)
        {
            if (snapshot.CompletedUtc < latest)
            {
                continue;
            }

            if (ContainsPath(snapshot, key))
            {
                latest = snapshot.CompletedUtc;
            }
        }

        return latest;
    }

    private static void IndexPresence(ScanResult snapshot, Dictionary<string, DateTime> presence)
    {
        var completed = snapshot.CompletedUtc;
        foreach (var file in snapshot.AllFiles)
        {
            Touch(presence, file.FullPath, completed);
        }

        foreach (var folder in FolderFilesNode.FoldersForSearch(
                     [snapshot.Root],
                     selected: null,
                     selectedFolderScope: false))
        {
            Touch(presence, folder.FullPath, completed);
        }
    }

    private static void Touch(Dictionary<string, DateTime> presence, string fullPath, DateTime completed)
    {
        var key = ScanCache.CanonicalKey(fullPath);
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        if (!presence.TryGetValue(key, out var existing) || completed > existing)
        {
            presence[key] = completed;
        }
    }

    private static bool ContainsPath(ScanResult snapshot, string canonicalKey)
    {
        foreach (var file in snapshot.AllFiles)
        {
            if (canonicalKey.Equals(ScanCache.CanonicalKey(file.FullPath), LocalPathGuard.Comparison))
            {
                return true;
            }
        }

        foreach (var folder in FolderFilesNode.FoldersForSearch(
                     [snapshot.Root],
                     selected: null,
                     selectedFolderScope: false))
        {
            if (canonicalKey.Equals(ScanCache.CanonicalKey(folder.FullPath), LocalPathGuard.Comparison))
            {
                return true;
            }
        }

        return false;
    }
}
