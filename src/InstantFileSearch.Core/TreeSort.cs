namespace InstantFileSearch;

public enum TreeSortMode
{
    NameAscending = 0,
    NameDescending = 1,
    SizeDescending = 2,
    SizeAscending = 3,
}

/// <summary>
/// Left-tree order for scan roots and each folder’s <see cref="FolderNode.TreeChildren"/>
/// (real subfolders plus the FILES row). Default matches the previous largest-first tree.
/// </summary>
public static class TreeSort
{
    public const TreeSortMode Default = TreeSortMode.SizeDescending;

    public const string NameAscLabel = "A–Z";
    public const string NameDescLabel = "Z–A";
    public const string SizeDescLabel = "Size ↓";
    public const string SizeAscLabel = "Size ↑";

    public static IReadOnlyList<string> Labels { get; } =
        [NameAscLabel, NameDescLabel, SizeDescLabel, SizeAscLabel];

    public static string Label(TreeSortMode mode) => mode switch
    {
        TreeSortMode.NameAscending => NameAscLabel,
        TreeSortMode.NameDescending => NameDescLabel,
        TreeSortMode.SizeAscending => SizeAscLabel,
        _ => SizeDescLabel,
    };

    public static TreeSortMode Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Default;
        }

        var value = text.Trim();
        if (value.Equals(NameAscLabel, StringComparison.OrdinalIgnoreCase)
            || value.Equals("A-Z", StringComparison.OrdinalIgnoreCase)
            || value.Equals(nameof(TreeSortMode.NameAscending), StringComparison.OrdinalIgnoreCase))
        {
            return TreeSortMode.NameAscending;
        }

        if (value.Equals(NameDescLabel, StringComparison.OrdinalIgnoreCase)
            || value.Equals("Z-A", StringComparison.OrdinalIgnoreCase)
            || value.Equals(nameof(TreeSortMode.NameDescending), StringComparison.OrdinalIgnoreCase))
        {
            return TreeSortMode.NameDescending;
        }

        if (value.Equals(SizeAscLabel, StringComparison.OrdinalIgnoreCase)
            || value.Equals("Size ^", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Min-Max", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Min–Max", StringComparison.OrdinalIgnoreCase)
            || value.Equals(nameof(TreeSortMode.SizeAscending), StringComparison.OrdinalIgnoreCase))
        {
            return TreeSortMode.SizeAscending;
        }

        if (value.Equals(SizeDescLabel, StringComparison.OrdinalIgnoreCase)
            || value.Equals("Size v", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Max-Min", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Max–Min", StringComparison.OrdinalIgnoreCase)
            || value.Equals(nameof(TreeSortMode.SizeDescending), StringComparison.OrdinalIgnoreCase))
        {
            return TreeSortMode.SizeDescending;
        }

        return Default;
    }

    public static int Compare(FolderNode a, FolderNode b, TreeSortMode mode)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var primary = mode switch
        {
            TreeSortMode.NameAscending => CompareName(a.Name, b.Name),
            TreeSortMode.NameDescending => CompareName(b.Name, a.Name),
            TreeSortMode.SizeAscending => a.Size.CompareTo(b.Size),
            _ => b.Size.CompareTo(a.Size),
        };
        if (primary != 0)
        {
            return primary;
        }

        var name = CompareName(a.Name, b.Name);
        if (name != 0)
        {
            return name;
        }

        return string.Compare(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Re-sort this node and every descendant’s tree rows. No-op on FILES.</summary>
    public static void Apply(FolderNode node, TreeSortMode mode)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.IsFilesNode)
        {
            return;
        }

        node.TreeChildren.Sort((left, right) => Compare(left, right, mode));
        foreach (var child in node.TreeChildren)
        {
            Apply(child, mode);
        }
    }

    public static IReadOnlyList<FolderNode> OrderRoots(IEnumerable<FolderNode> roots, TreeSortMode mode)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var list = roots.ToList();
        list.Sort((left, right) => Compare(left, right, mode));
        return list;
    }

    private static int CompareName(string left, string right) =>
        string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
}
