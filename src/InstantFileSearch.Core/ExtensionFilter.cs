namespace InstantFileSearch;

/// <summary>
/// Ext box / CLI <c>--ext</c>: match the last <c>.</c> segment of a file name.
/// Folders have no extension. Invalid tokens are skipped, not errors.
/// </summary>
public static class ExtensionFilter
{
    public static IReadOnlyList<string> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var raw in Split(text))
        {
            if (!TryNormalize(raw, out var ext) || !seen.Add(ext))
            {
                continue;
            }

            list.Add(ext);
        }

        return list;
    }

    public static bool HasAny(string? text) => Parse(text).Count > 0;

    public static bool Matches(string name, bool isFolder, IReadOnlyList<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(extensions);
        if (extensions.Count == 0)
        {
            return true;
        }

        if (isFolder)
        {
            return false;
        }

        var ext = OfName(name);
        if (ext.Length == 0)
        {
            return false;
        }

        foreach (var wanted in extensions)
        {
            if (ext.Equals(wanted, FileNameSearch.Comparison))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Last <c>.</c> segment, without the dot. Empty when there is no extension
    /// (no dot, or a trailing dot). A leading-dot name such as <c>.gitignore</c>
    /// uses <c>gitignore</c>.
    /// </summary>
    public static string OfName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var i = name.LastIndexOf('.');
        if (i < 0 || i == name.Length - 1)
        {
            return "";
        }

        return name[(i + 1)..];
    }

    private static IEnumerable<string> Split(string text)
    {
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && !IsSeparator(text[i]))
            {
                continue;
            }

            if (i > start)
            {
                yield return text[start..i];
            }

            start = i + 1;
        }
    }

    private static bool IsSeparator(char c) =>
        c is ',' or ';' || char.IsWhiteSpace(c);

    /// <summary>
    /// Leading dots optional. Skip empty, path separators, wildcards, extra dots,
    /// quotes, and colons — those are not file extensions.
    /// </summary>
    private static bool TryNormalize(string raw, out string ext)
    {
        ext = "";
        var trimmed = raw.Trim();
        var i = 0;
        while (i < trimmed.Length && trimmed[i] == '.')
        {
            i++;
        }

        if (i >= trimmed.Length)
        {
            return false;
        }

        var candidate = trimmed[i..];
        foreach (var c in candidate)
        {
            if (c is '.' or '/' or '\\' or '*' or '?' or ':' or '"' or '<' or '>' or '|')
            {
                return false;
            }
        }

        ext = candidate;
        return true;
    }
}
