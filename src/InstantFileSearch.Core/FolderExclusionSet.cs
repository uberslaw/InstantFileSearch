namespace InstantFileSearch;

public sealed class FolderExclusionSet
{
    private readonly HashSet<string> _paths = new(LocalPathGuard.Comparison switch
    {
        StringComparison.OrdinalIgnoreCase => StringComparer.OrdinalIgnoreCase,
        _ => StringComparer.Ordinal,
    });

    public IReadOnlyCollection<string> Items => _paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();

    public int Count => _paths.Count;

    public static FolderExclusionSet From(IEnumerable<string>? paths)
    {
        var set = new FolderExclusionSet();
        if (paths is null)
        {
            return set;
        }

        foreach (var path in paths)
        {
            set.Add(path);
        }

        return set;
    }

    public bool Add(string path)
    {
        if (!LocalPathGuard.TryGetFullPath(path, out var full))
        {
            return false;
        }

        return _paths.Add(full);
    }

    public bool Remove(string path)
    {
        if (!LocalPathGuard.TryGetFullPath(path, out var full))
        {
            return false;
        }

        return _paths.Remove(full);
    }

    public bool Contains(string path)
    {
        if (!LocalPathGuard.TryGetFullPath(path, out var full))
        {
            return false;
        }

        return _paths.Contains(full);
    }
}
