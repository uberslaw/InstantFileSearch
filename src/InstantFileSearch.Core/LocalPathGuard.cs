namespace InstantFileSearch;

/// <summary>
/// Cheap local-path checks for scan roots and "open this result" actions.
/// No network, no ACLs — reject empty/invalid paths and stay inside the current scan.
/// </summary>
public static class LocalPathGuard
{
    public static StringComparison Comparison { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public static bool TryGetFullPath(string? path, out string fullPath)
    {
        fullPath = "";
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var resolved = CanonicalizeFullPath(Path.GetFullPath(NormalizeInput(path)));
            if (string.IsNullOrWhiteSpace(resolved))
            {
                return false;
            }

            fullPath = resolved;
            return true;
        }
        catch (Exception ex) when (
            ex is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or IOException)
        {
            return false;
        }
    }

    public static bool TryResolveExistingDirectory(string? path, out string fullPath)
    {
        fullPath = "";
        if (!TryGetFullPath(path, out var resolved) || !Directory.Exists(resolved))
        {
            return false;
        }

        fullPath = resolved;
        return true;
    }

    public static bool TryResolveExistingFileOrDirectory(string? path, out string fullPath)
    {
        fullPath = "";
        if (!TryGetFullPath(path, out var resolved))
        {
            return false;
        }

        if (!File.Exists(resolved) && !Directory.Exists(resolved))
        {
            return false;
        }

        fullPath = resolved;
        return true;
    }

    public static bool IsSameOrUnder(string candidatePath, string rootPath)
    {
        if (!TryGetFullPath(candidatePath, out var candidate) ||
            !TryGetFullPath(rootPath, out var root))
        {
            return false;
        }

        if (candidate.Equals(root, Comparison))
        {
            return true;
        }

        var prefix = root.EndsWith(Path.DirectorySeparatorChar) || root.EndsWith(Path.AltDirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return candidate.StartsWith(prefix, Comparison);
    }

    public static bool IsIndexedPath(string? path, ScanResult scan)
    {
        if (!TryGetFullPath(path, out var fullPath) || !IsSameOrUnder(fullPath, scan.Root.FullPath))
        {
            return false;
        }

        foreach (var file in scan.AllFiles)
        {
            if (TryGetFullPath(file.FullPath, out var filePath) &&
                filePath.Equals(fullPath, Comparison))
            {
                return true;
            }
        }

        return FolderTreeContains(scan.Root, fullPath);
    }

    public static bool TryValidateOpenPath(string? path, ScanResult? scan, out string fullPath)
    {
        fullPath = "";
        if (scan is null || !TryResolveExistingFileOrDirectory(path, out var resolved))
        {
            return false;
        }

        if (!IsIndexedPath(resolved, scan))
        {
            return false;
        }

        fullPath = resolved;
        return true;
    }

    /// <summary>
    /// <c>\\?\C:\</c>, <c>\\.\C:\</c>, and <c>\\?\UNC\server\share</c> are the same
    /// locations as the unprefixed paths. Callers must not treat the prefix as a
    /// different root.
    /// </summary>
    public static string StripExtendedPrefix(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[8..];
        }

        if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase))
        {
            return path[4..];
        }

        return path;
    }

    /// <summary>
    /// <see cref="Path.GetFullPath(string)"/> may keep a trailing slash. Treat
    /// <c>/tmp/foo</c> and <c>/tmp/foo/</c> as the same location, but keep the
    /// slash on volume roots (<c>/</c>, <c>C:\</c>) so "under root" still works.
    /// </summary>
    public static string CanonicalizeFullPath(string path)
    {
        var stripped = StripExtendedPrefix(path);
        var volumeRoot = Path.GetPathRoot(stripped);
        var trimmed = stripped.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrEmpty(volumeRoot))
        {
            return trimmed;
        }

        var rootTrimmed = volumeRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.Length == 0 || trimmed.Equals(rootTrimmed, Comparison))
        {
            return volumeRoot;
        }

        return trimmed;
    }

    private static string NormalizeInput(string path)
    {
        var trimmed = StripExtendedPrefix(path.Trim());
        if (OperatingSystem.IsWindows() && IsDriveDesignator(trimmed))
        {
            return trimmed + "\\";
        }

        return trimmed;
    }

    /// <summary>
    /// On Windows, <c>C:</c> is the process current directory on that drive, not
    /// <c>C:\</c>. Operators typing a drive mean the volume root.
    /// </summary>
    private static bool IsDriveDesignator(string path) =>
        path.Length == 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';

    private static bool FolderTreeContains(FolderNode node, string fullPath)
    {
        if (TryGetFullPath(node.FullPath, out var folderPath) &&
            folderPath.Equals(fullPath, Comparison))
        {
            return true;
        }

        foreach (var child in node.Folders)
        {
            if (FolderTreeContains(child, fullPath))
            {
                return true;
            }
        }

        return false;
    }
}
