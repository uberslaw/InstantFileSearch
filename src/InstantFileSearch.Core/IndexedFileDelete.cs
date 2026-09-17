namespace InstantFileSearch;

/// <summary>
/// Delete is files only (not folders, not Exclude, not Remove from list).
/// Recycle Bin is for local Windows paths; UNC is permanently deleted because
/// the Recycle Bin does not keep network files.
/// </summary>
public static class IndexedFileDelete
{
    public static bool UsesRecycleBin(string? path) =>
        OperatingSystem.IsWindows() && !UncPath.IsUnc(path);

    public static string ConfirmMessage(string name, string path, bool recycleBin)
    {
        var body = recycleBin
            ? $"Move '{name}' to the Recycle Bin?\n\n{path}"
            : $"Permanently delete '{name}'?\n\n{path}\n\nThis cannot be undone.";
        return body + "\n\nThis is not Exclude or Remove from list.";
    }

    public static bool IsIndexedFile(ScanResult scan, string fullPath, out FileEntry? file)
    {
        file = null;
        if (!LocalPathGuard.TryGetFullPath(fullPath, out var resolved))
        {
            return false;
        }

        foreach (var entry in scan.AllFiles)
        {
            if (LocalPathGuard.TryGetFullPath(entry.FullPath, out var filePath)
                && filePath.Equals(resolved, LocalPathGuard.Comparison))
            {
                file = entry;
                return true;
            }
        }

        return false;
    }

    public static bool TryRemoveFromIndex(ScanResult scan, string fullPath, out ScanResult updated)
    {
        updated = scan;
        if (!IsIndexedFile(scan, fullPath, out var file) || file is null)
        {
            return false;
        }

        var parent = file.Parent;
        if (parent is null)
        {
            return false;
        }

        if (!parent.Files.Remove(file))
        {
            var match = parent.Files.Find(item =>
                LocalPathGuard.TryGetFullPath(item.FullPath, out var itemPath)
                && LocalPathGuard.TryGetFullPath(fullPath, out var want)
                && itemPath.Equals(want, LocalPathGuard.Comparison));
            if (match is null || !parent.Files.Remove(match))
            {
                return false;
            }

            file = match;
        }

        var size = file.Size;
        for (var walk = parent; walk is not null; walk = walk.Parent)
        {
            walk.Size = Math.Max(0, walk.Size - size);
            walk.FileCount = Math.Max(0, walk.FileCount - 1);
        }

        FolderFilesNode.Attach(scan.Root);
        var remaining = scan.AllFiles
            .Where(item => !ReferenceEquals(item, file)
                && !(LocalPathGuard.TryGetFullPath(item.FullPath, out var itemPath)
                    && LocalPathGuard.TryGetFullPath(fullPath, out var want)
                    && itemPath.Equals(want, LocalPathGuard.Comparison)))
            .ToList();

        updated = new ScanResult
        {
            Root = scan.Root,
            AllFiles = remaining,
            Duration = scan.Duration,
            ErrorCount = scan.ErrorCount,
            CompletedUtc = scan.CompletedUtc,
        };
        return true;
    }

    /// <summary>
    /// Deletes on disk first; the index is updated only after the deleter succeeds
    /// so a failed delete cannot hide a file that is still there.
    /// </summary>
    public static bool TryDeleteIndexedFile(
        ScanResult scan,
        string fullPath,
        IFileDeleter deleter,
        out ScanResult updated)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(deleter);
        updated = scan;
        if (!IsIndexedFile(scan, fullPath, out _)
            || !LocalPathGuard.TryGetFullPath(fullPath, out var resolved))
        {
            return false;
        }

        deleter.Delete(resolved);
        return TryRemoveFromIndex(scan, resolved, out updated);
    }
}
