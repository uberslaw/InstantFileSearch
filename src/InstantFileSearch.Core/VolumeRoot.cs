namespace InstantFileSearch;

/// <summary>
/// Same volume = rename (NTFS / same UNC share). Different drive letter or
/// different UNC share = copy. Does not call DriveInfo or the filesystem.
/// Linux tests use fake <c>C:\</c> and <c>\\server\share</c> strings.
/// </summary>
public static class VolumeRoot
{
    public static bool TryGet(string? path, out string volumeRoot)
    {
        volumeRoot = "";
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var trimmed = LocalPathGuard.StripExtendedPrefix(path.Trim());
        if (UncPath.TryNormalize(trimmed, out var unc))
        {
            volumeRoot = ShareRoot(unc);
            return volumeRoot.Length > 0;
        }

        if (IndexedPath.LooksLikeDrive(trimmed)
            && IndexedPath.TryCanonicalize(trimmed, out var drivePath))
        {
            volumeRoot = drivePath.Length >= 3 ? drivePath[..3] : drivePath;
            if (volumeRoot.Length == 2 && volumeRoot[1] == ':')
            {
                volumeRoot += "\\";
            }

            return volumeRoot.Length == 3;
        }

        if (LocalPathGuard.TryGetFullPath(trimmed, out var full))
        {
            var root = Path.GetPathRoot(full);
            if (!string.IsNullOrEmpty(root))
            {
                volumeRoot = root;
                return true;
            }
        }

        return false;
    }

    public static bool AreSame(string? first, string? second)
    {
        if (!TryGet(first, out var a) || !TryGet(second, out var b))
        {
            return false;
        }

        return a.Equals(b, LocalPathGuard.Comparison);
    }

    /// <summary>
    /// <c>\\server\share</c> for any path on that share. Different share names
    /// are different volumes even on the same server.
    /// </summary>
    public static string ShareRoot(string normalizedUnc)
    {
        if (!UncPath.TryNormalize(normalizedUnc, out var unc))
        {
            return "";
        }

        var rest = unc[2..];
        var serverEnd = rest.IndexOf('\\');
        if (serverEnd < 0 || serverEnd + 1 >= rest.Length)
        {
            return unc;
        }

        var afterServer = rest[(serverEnd + 1)..];
        var shareEnd = afterServer.IndexOf('\\');
        if (shareEnd < 0)
        {
            return unc;
        }

        return @"\\" + rest[..serverEnd] + @"\" + afterServer[..shareEnd];
    }
}
