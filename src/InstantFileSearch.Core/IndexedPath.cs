namespace InstantFileSearch;

/// <summary>
/// Path join/compare that understands Windows drive letters and UNC on Linux.
/// <see cref="Path.GetFullPath(string)"/> treats <c>C:\foo</c> as cwd-relative
/// on Unix, so merge planning must not use it for those roots.
/// </summary>
public static class IndexedPath
{
    public static bool TryCanonicalize(string? path, out string canonical)
    {
        canonical = "";
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var trimmed = LocalPathGuard.StripExtendedPrefix(path.Trim());
        if (UncPath.TryNormalize(trimmed, out var unc))
        {
            canonical = unc;
            return true;
        }

        if (TryCanonicalizeDrive(trimmed, out canonical))
        {
            return true;
        }

        return LocalPathGuard.TryGetFullPath(trimmed, out canonical);
    }

    public static char Separator(string path)
    {
        if (UncPath.LooksLikeUnc(path) || LooksLikeDrive(path))
        {
            return '\\';
        }

        return Path.DirectorySeparatorChar;
    }

    public static string Combine(string root, string relative)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (string.IsNullOrWhiteSpace(relative))
        {
            return root;
        }

        var sep = Separator(root);
        var rel = relative.Replace('/', sep).Replace('\\', sep).Trim(sep);
        if (rel.Length == 0)
        {
            return root;
        }

        var trimmedRoot = root.TrimEnd('/', '\\');
        if (LooksLikeDrive(root) && trimmedRoot.Length == 2 && trimmedRoot[1] == ':')
        {
            return trimmedRoot + sep + rel;
        }

        return trimmedRoot + sep + rel;
    }

    public static bool TryGetParent(string path, out string parent)
    {
        parent = "";
        if (!TryCanonicalize(path, out var canonical))
        {
            return false;
        }

        var sep = Separator(canonical);
        var last = canonical.LastIndexOf(sep);
        if (last < 0)
        {
            return false;
        }

        if (LooksLikeDrive(canonical) && last == 2)
        {
            parent = canonical[..2] + "\\";
            return true;
        }

        if (UncPath.TryNormalize(canonical, out var unc))
        {
            var shareRoot = VolumeRoot.ShareRoot(unc);
            if (canonical.Equals(shareRoot, LocalPathGuard.Comparison))
            {
                return false;
            }

            parent = last == 0 ? canonical : canonical[..last];
            if (parent.Equals(shareRoot, LocalPathGuard.Comparison)
                || parent.Length < shareRoot.Length)
            {
                parent = shareRoot;
            }

            return parent.Length > 0;
        }

        if (last == 0)
        {
            parent = sep.ToString();
            return true;
        }

        parent = canonical[..last];
        return parent.Length > 0;
    }

    public static bool TryGetRelative(string fullPath, string folderPath, out string relative)
    {
        relative = "";
        if (!TryCanonicalize(fullPath, out var file)
            || !TryCanonicalize(folderPath, out var folder))
        {
            return false;
        }

        if (file.Equals(folder, LocalPathGuard.Comparison))
        {
            return false;
        }

        if (!IsSameOrUnder(file, folder))
        {
            return false;
        }

        var sep = Separator(folder);
        var prefix = folder.EndsWith(sep) ? folder : folder + sep;
        relative = file[prefix.Length..];
        return relative.Length > 0;
    }

    public static bool IsSameOrUnder(string candidatePath, string rootPath)
    {
        if (!TryCanonicalize(candidatePath, out var candidate)
            || !TryCanonicalize(rootPath, out var root))
        {
            return false;
        }

        if (candidate.Equals(root, LocalPathGuard.Comparison))
        {
            return true;
        }

        var sep = Separator(root);
        var prefix = root.EndsWith(sep) ? root : root + sep;
        return candidate.StartsWith(prefix, LocalPathGuard.Comparison);
    }

    public static string[] SplitRelative(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
        {
            return [];
        }

        return relative.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
    }

    public static string RelativeDirectory(string relativePath)
    {
        var parts = SplitRelative(relativePath);
        if (parts.Length <= 1)
        {
            return "";
        }

        var sep = relativePath.Contains('\\') ? '\\' : Path.DirectorySeparatorChar;
        return string.Join(sep, parts.Take(parts.Length - 1));
    }

    public static bool LooksLikeDrive(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var trimmed = LocalPathGuard.StripExtendedPrefix(path.Trim());
        return trimmed.Length >= 2
            && char.IsAsciiLetter(trimmed[0])
            && trimmed[1] == ':';
    }

    private static bool TryCanonicalizeDrive(string path, out string canonical)
    {
        canonical = "";
        if (!LooksLikeDrive(path))
        {
            return false;
        }

        var normalized = path.Replace('/', '\\');
        var drive = char.ToUpperInvariant(normalized[0]);
        var rest = normalized.Length <= 2 ? "" : normalized[2..].Trim('\\');
        var segments = new List<string>();
        foreach (var part in rest.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (segments.Count == 0)
                {
                    return false;
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(part);
        }

        canonical = drive + @":\";
        if (segments.Count > 0)
        {
            canonical += string.Join('\\', segments);
        }

        return true;
    }
}
