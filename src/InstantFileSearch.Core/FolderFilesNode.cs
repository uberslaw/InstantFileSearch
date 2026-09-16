namespace InstantFileSearch;

/// <summary>
/// TreeSize-style <c>FILES</c> row: one synthetic child per folder that has
/// direct files, sized as those files only. Not a filesystem path.
/// </summary>
public static class FolderFilesNode
{
    public const string DisplayName = "FILES";

    public static void Attach(FolderNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.IsFilesNode)
        {
            node.TreeChildren.Clear();
            return;
        }

        foreach (var child in node.Folders)
        {
            Attach(child);
        }

        node.TreeChildren.Clear();
        if (node.Files.Count > 0)
        {
            node.TreeChildren.Add(Create(node));
        }

        node.TreeChildren.AddRange(node.Folders);
        node.TreeChildren.Sort((a, b) => b.Size.CompareTo(a.Size));
    }

    public static FolderNode Create(FolderNode parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        long size = 0;
        var modified = DateTime.MinValue;
        foreach (var file in parent.Files)
        {
            size += file.Size;
            if (file.Modified > modified)
            {
                modified = file.Modified;
            }
        }

        var filesNode = new FolderNode
        {
            Name = DisplayName,
            FullPath = parent.FullPath,
            Parent = parent,
            Size = size,
            FileCount = parent.Files.Count,
            FolderCount = 0,
            Modified = modified,
            IsFilesNode = true,
            LocationKind = ScanLocationKind.Unknown,
        };
        filesNode.Files.AddRange(parent.Files);
        return filesNode;
    }

    public static IReadOnlyList<FolderNode> ContentFolders(FolderNode selected) =>
        selected.IsFilesNode ? [] : selected.Folders;

    public static IReadOnlyList<FileEntry> ContentFiles(FolderNode selected) =>
        selected.IsFilesNode
            ? selected.Parent?.Files ?? selected.Files
            : selected.Files;

    public static bool CanExclude(FolderNode? node) =>
        node is { Parent: not null, IsFilesNode: false };

    public static bool IsScanRoot(FolderNode? node) =>
        node is { Parent: null, IsFilesNode: false };

    public static string OwnerName(FolderNode node) =>
        node.IsFilesNode
            ? node.Parent?.Name ?? "this folder"
            : node.Name;

    public static IEnumerable<FileEntry> FilesForSearch(
        IEnumerable<FileEntry> allFiles,
        FolderNode? selected,
        bool selectedFolderScope)
    {
        if (selectedFolderScope && selected is { IsFilesNode: true })
        {
            return selected.Parent?.Files ?? selected.Files;
        }

        return allFiles;
    }

    /// <summary>
    /// Real folders for search. Never yields FILES. Selected-folder scope lists
    /// descendants of that folder (not the folder itself). FILES selected → none.
    /// </summary>
    public static IEnumerable<FolderNode> FoldersForSearch(
        IEnumerable<FolderNode> roots,
        FolderNode? selected,
        bool selectedFolderScope)
    {
        ArgumentNullException.ThrowIfNull(roots);
        if (selectedFolderScope)
        {
            if (selected is null || selected.IsFilesNode)
            {
                yield break;
            }

            foreach (var node in EnumerateFolders(selected, includeSelf: false))
            {
                yield return node;
            }

            yield break;
        }

        foreach (var root in roots)
        {
            foreach (var node in EnumerateFolders(root, includeSelf: true))
            {
                yield return node;
            }
        }
    }

    private static IEnumerable<FolderNode> EnumerateFolders(FolderNode node, bool includeSelf)
    {
        if (node.IsFilesNode)
        {
            yield break;
        }

        if (includeSelf)
        {
            yield return node;
        }

        foreach (var child in node.Folders)
        {
            foreach (var nested in EnumerateFolders(child, includeSelf: true))
            {
                yield return nested;
            }
        }
    }
}
