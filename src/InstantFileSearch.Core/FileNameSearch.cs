using System.IO.Enumeration;

namespace InstantFileSearch;

public static class FileNameSearch
{
    public static IEnumerable<FileEntry> Filter(
        IEnumerable<FileEntry> files,
        string query,
        int limit = 5000)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            yield break;
        }

        var pattern = query.Trim();
        var wildcard = pattern.Contains('*') || pattern.Contains('?');
        var taken = 0;

        foreach (var file in files)
        {
            if (taken >= limit)
            {
                yield break;
            }

            if (Matches(file, pattern, wildcard))
            {
                taken++;
                yield return file;
            }
        }
    }

    public static bool Matches(FileEntry file, string pattern, bool wildcard)
    {
        if (wildcard)
        {
            return FileSystemName.MatchesSimpleExpression(pattern, file.Name, ignoreCase: true)
                   || FileSystemName.MatchesSimpleExpression(pattern, file.FullPath, ignoreCase: true);
        }

        return file.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase)
               || file.FullPath.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }
}
