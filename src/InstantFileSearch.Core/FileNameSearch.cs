using System.IO.Enumeration;

namespace InstantFileSearch;

public static class FileNameSearch
{
    public const int DefaultLimit = 5000;

    public static IEnumerable<FileEntry> Filter(
        IEnumerable<FileEntry> files,
        string query,
        int limit = DefaultLimit) =>
        Filter(files, new SearchQuery { Text = query }, limit);

    public static IEnumerable<FileEntry> Filter(
        IEnumerable<FileEntry> files,
        SearchQuery query,
        int limit = DefaultLimit)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!query.HasCriteria)
        {
            yield break;
        }

        var pattern = query.Text.Trim();
        var hasText = pattern.Length > 0;
        var wildcard = hasText && (pattern.Contains('*') || pattern.Contains('?'));
        var taken = 0;

        foreach (var file in files)
        {
            if (taken >= limit)
            {
                yield break;
            }

            var sizeOk = MatchesSize(file, query);
            var modifiedOk = MatchesModified(file, query);
            var folderOk = MatchesFolder(file, query);
            var textOk = !hasText || Matches(file, pattern, wildcard, query.Match);
            if (!sizeOk || !modifiedOk || !folderOk || !textOk)
            {
                continue;
            }

            taken++;
            yield return file;
        }
    }

    public static bool Matches(FileEntry file, string pattern, bool wildcard) =>
        Matches(file, pattern, wildcard, SearchMatchMode.NameOrPath);

    public static bool Matches(FileEntry file, string pattern, bool wildcard, SearchMatchMode match)
    {
        var name = match != SearchMatchMode.Path;
        var path = match != SearchMatchMode.Name;

        if (wildcard)
        {
            return (name && FileSystemName.MatchesSimpleExpression(pattern, file.Name, ignoreCase: true))
                   || (path && FileSystemName.MatchesSimpleExpression(pattern, file.FullPath, ignoreCase: true));
        }

        return (name && file.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
               || (path && file.FullPath.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesSize(FileEntry file, SearchQuery query)
    {
        if (query.MinSizeBytes is { } min && file.Size < min)
        {
            return false;
        }

        if (query.MaxSizeBytes is { } max && file.Size > max)
        {
            return false;
        }

        return true;
    }

    private static bool MatchesModified(FileEntry file, SearchQuery query)
    {
        var modified = file.Modified.Date;
        if (query.ModifiedFrom is { } from && modified < from.Date)
        {
            return false;
        }

        if (query.ModifiedTo is { } to && modified > to.Date)
        {
            return false;
        }

        return true;
    }

    private static bool MatchesFolder(FileEntry file, SearchQuery query)
    {
        if (query.UnderFolder is null)
        {
            return true;
        }

        if (!LocalPathGuard.IsSameOrUnder(file.FullPath, query.UnderFolder))
        {
            return false;
        }

        if (!query.DirectChildrenOnly)
        {
            return true;
        }

        return file.Parent is not null
            && LocalPathGuard.TryGetFullPath(file.Parent.FullPath, out var parent)
            && LocalPathGuard.TryGetFullPath(query.UnderFolder, out var under)
            && parent.Equals(under, LocalPathGuard.Comparison);
    }
}
