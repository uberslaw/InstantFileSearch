using System.IO.Enumeration;

namespace InstantFileSearch;

public static class FileNameSearch
{
    public const int DefaultLimit = 5000;
    public const StringComparison Comparison = StringComparison.OrdinalIgnoreCase;

    public static IEnumerable<FileEntry> Filter(
        IEnumerable<FileEntry> files,
        string query,
        int limit = DefaultLimit) =>
        Filter(files, new SearchQuery { Text = query }, limit);

    public static IEnumerable<FileEntry> Filter(
        IEnumerable<FileEntry> files,
        SearchQuery query,
        int limit = DefaultLimit) =>
        FilterHits([], files, query, limit)
            .Select(hit => hit.File!);

    public static IEnumerable<SearchHit> FilterHits(
        IEnumerable<FolderNode> folders,
        IEnumerable<FileEntry> files,
        SearchQuery query,
        int limit = DefaultLimit)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(query);

        if (!query.HasCriteria)
        {
            yield break;
        }

        var pattern = query.Text.Trim();
        var hasText = pattern.Length > 0;
        var wildcard = HasWildcard(pattern);
        var taken = 0;

        foreach (var folder in folders)
        {
            if (taken >= limit)
            {
                yield break;
            }

            if (folder.IsFilesNode)
            {
                continue;
            }

            if (!MatchesEntry(
                    folder.Name,
                    folder.FullPath,
                    folder.Size,
                    folder.Modified,
                    folder.Parent?.FullPath,
                    pattern,
                    hasText,
                    wildcard,
                    query))
            {
                continue;
            }

            taken++;
            yield return SearchHit.FromFolder(folder);
        }

        foreach (var file in files)
        {
            if (taken >= limit)
            {
                yield break;
            }

            if (!MatchesEntry(
                    file.Name,
                    file.FullPath,
                    file.Size,
                    file.Modified,
                    file.Parent?.FullPath,
                    pattern,
                    hasText,
                    wildcard,
                    query))
            {
                continue;
            }

            taken++;
            yield return SearchHit.FromFile(file);
        }
    }

    public static bool HasWildcard(string pattern) =>
        pattern.Contains('*') || pattern.Contains('?');

    public static bool Matches(FileEntry file, string pattern, bool wildcard) =>
        Matches(file, pattern, wildcard, SearchMatchMode.NameOrPath);

    public static bool Matches(FileEntry file, string pattern, bool wildcard, SearchMatchMode match) =>
        Matches(file.Name, file.FullPath, pattern, wildcard, match);

    public static bool Matches(
        string name,
        string fullPath,
        string pattern,
        bool wildcard,
        SearchMatchMode match)
    {
        var useName = match != SearchMatchMode.Path;
        var usePath = match != SearchMatchMode.Name;

        if (wildcard)
        {
            return (useName && FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true))
                   || (usePath && FileSystemName.MatchesSimpleExpression(pattern, fullPath, ignoreCase: true));
        }

        return (useName && name.Equals(pattern, Comparison))
               || (usePath && fullPath.Equals(pattern, Comparison));
    }

    private static bool MatchesEntry(
        string name,
        string fullPath,
        long size,
        DateTime modified,
        string? parentPath,
        string pattern,
        bool hasText,
        bool wildcard,
        SearchQuery query)
    {
        if (!MatchesSize(size, query) || !MatchesModified(modified, query) || !MatchesFolder(fullPath, parentPath, query))
        {
            return false;
        }

        return !hasText || Matches(name, fullPath, pattern, wildcard, query.Match);
    }

    private static bool MatchesSize(long size, SearchQuery query)
    {
        if (query.MinSizeBytes is { } min && size < min)
        {
            return false;
        }

        if (query.MaxSizeBytes is { } max && size > max)
        {
            return false;
        }

        return true;
    }

    private static bool MatchesModified(DateTime modified, SearchQuery query)
    {
        var day = modified.Date;
        if (query.ModifiedFrom is { } from && day < from.Date)
        {
            return false;
        }

        if (query.ModifiedTo is { } to && day > to.Date)
        {
            return false;
        }

        return true;
    }

    private static bool MatchesFolder(string fullPath, string? parentPath, SearchQuery query)
    {
        if (query.UnderFolder is null)
        {
            return true;
        }

        if (!LocalPathGuard.IsSameOrUnder(fullPath, query.UnderFolder))
        {
            return false;
        }

        if (!query.DirectChildrenOnly)
        {
            return true;
        }

        return parentPath is not null
            && LocalPathGuard.TryGetFullPath(parentPath, out var parent)
            && LocalPathGuard.TryGetFullPath(query.UnderFolder, out var under)
            && parent.Equals(under, LocalPathGuard.Comparison);
    }
}
