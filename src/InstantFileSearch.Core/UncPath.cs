namespace InstantFileSearch;

/// <summary>
/// UNC folder syntax that does not call the filesystem. Linux <see cref="Path.GetFullPath(string)"/>
/// treats <c>\\server\share</c> as a relative path; callers must use this instead.
/// </summary>
public static class UncPath
{
    public static bool LooksLikeUnc(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var trimmed = LocalPathGuard.StripExtendedPrefix(path.Trim());
        return trimmed.StartsWith(@"\\", StringComparison.Ordinal)
            || trimmed.StartsWith("//", StringComparison.Ordinal);
    }

    public static bool TryNormalize(string? path, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var raw = LocalPathGuard.StripExtendedPrefix(path.Trim()).Replace('/', '\\');
        if (!raw.StartsWith(@"\\", StringComparison.Ordinal) || raw.Length < 5)
        {
            return false;
        }

        var rest = raw[2..];
        if (rest.StartsWith('\\'))
        {
            return false;
        }

        if (!TryReadServer(rest, out var server, out var shareOffset))
        {
            return false;
        }

        var afterServer = rest[shareOffset..];
        if (afterServer.Length == 0)
        {
            return false;
        }

        var shareEnd = afterServer.IndexOf('\\');
        var share = shareEnd < 0 ? afterServer : afterServer[..shareEnd];
        if (!IsValidShare(share))
        {
            return false;
        }

        var remainder = shareEnd < 0 ? "" : afterServer[(shareEnd + 1)..];
        if (!TryCanonicalizeTail(remainder, out var tail))
        {
            return false;
        }

        normalized = @"\\" + server + @"\" + share;
        if (tail.Length > 0)
        {
            normalized += @"\" + tail;
        }

        return true;
    }

    public static bool IsUnc(string? path) => TryNormalize(path, out _);

    public static bool TryGetShareName(string? path, out string share)
    {
        share = "";
        if (!TryNormalize(path, out var normalized))
        {
            return false;
        }

        var rest = normalized[2..];
        var slash = rest.IndexOf('\\');
        if (slash < 0 || slash + 1 >= rest.Length)
        {
            return false;
        }

        var afterServer = rest[(slash + 1)..];
        var shareEnd = afterServer.IndexOf('\\');
        share = shareEnd < 0 ? afterServer : afterServer[..shareEnd];
        return share.Length > 0;
    }

    /// <summary>
    /// Drive admin shares (<c>C$</c>, <c>D$</c>) and <c>ADMIN$</c>. These usually need an
    /// elevated process and an account that can access the share. Not a custom SMB client.
    /// </summary>
    public static bool IsAdminShare(string? path)
    {
        if (!TryGetShareName(path, out var share))
        {
            return false;
        }

        if (share.Equals("ADMIN$", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return share.Length == 2
            && char.IsAsciiLetter(share[0])
            && share[1] == '$';
    }

    private static bool TryReadServer(string rest, out string server, out int shareOffset)
    {
        server = "";
        shareOffset = 0;
        if (rest.StartsWith('['))
        {
            var close = rest.IndexOf(']');
            if (close <= 1)
            {
                return false;
            }

            server = rest[..(close + 1)];
            if (close + 1 >= rest.Length || rest[close + 1] != '\\')
            {
                return false;
            }

            if (!IsValidIpv6Server(server))
            {
                return false;
            }

            shareOffset = close + 2;
            return true;
        }

        var slash = rest.IndexOf('\\');
        if (slash <= 0)
        {
            return false;
        }

        server = rest[..slash];
        if (!IsValidServer(server))
        {
            return false;
        }

        shareOffset = slash + 1;
        return true;
    }

    private static bool TryCanonicalizeTail(string remainder, out string tail)
    {
        tail = "";
        var segments = new List<string>();
        foreach (var part in remainder.Split('\\', StringSplitOptions.RemoveEmptyEntries))
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

            if (!IsValidSegment(part))
            {
                return false;
            }

            segments.Add(part);
        }

        tail = string.Join('\\', segments);
        return true;
    }

    private static bool IsValidServer(string server)
    {
        if (string.IsNullOrWhiteSpace(server) || server.Length > 255)
        {
            return false;
        }

        if (IsIpv4(server))
        {
            return true;
        }

        foreach (var ch in server)
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_')
            {
                continue;
            }

            return false;
        }

        return server[0] != '.' && server[0] != '-';
    }

    private static bool IsValidIpv6Server(string bracketed)
    {
        if (bracketed.Length < 4 || bracketed[0] != '[' || bracketed[^1] != ']')
        {
            return false;
        }

        var inner = bracketed[1..^1];
        return inner.Length > 0
            && inner.Contains(':')
            && inner.IndexOfAny(['\\', '/', ' ', '[', ']']) < 0;
    }

    private static bool IsIpv4(string server)
    {
        var parts = server.Split('.');
        if (parts.Length != 4)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (!byte.TryParse(part, out _))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidShare(string share) =>
        share.Length > 0 && share.Length <= 80 && IsValidSegment(share);

    private static bool IsValidSegment(string part)
    {
        if (part.Length == 0 || part.EndsWith(' ') || part.EndsWith('.'))
        {
            return false;
        }

        foreach (var ch in part)
        {
            if (char.IsControl(ch) || ch is '<' or '>' or ':' or '"' or '|' or '?' or '*' or '/' or '\\')
            {
                return false;
            }
        }

        return true;
    }
}
