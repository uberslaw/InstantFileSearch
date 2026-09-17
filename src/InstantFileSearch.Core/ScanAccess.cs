namespace InstantFileSearch;

/// <summary>
/// Readable scan-root errors. <see cref="Directory.Exists"/> is false for many
/// inaccessible admin shares, so the scanner must enumerate the root and wrap IO.
/// </summary>
public static class ScanAccess
{
    public static string Missing(string path)
    {
        if (UncPath.TryNormalize(path, out var unc))
        {
            if (UncPath.IsAdminShare(unc))
            {
                return $"Share not found or not accessible: {unc}. Admin shares (C$, D$) usually need Instant File Search running as administrator.";
            }

            return $"Share not found or not accessible: {unc}.";
        }

        return $"Folder not found: {path}";
    }

    public static string Denied(string path, bool elevated)
    {
        if (UncPath.IsAdminShare(path) && !elevated)
        {
            return $"Access denied: {path}. This looks like an admin share. Use File → Run as administrator, then scan again.";
        }

        if (!elevated)
        {
            return $"Access denied: {path}. Try File → Run as administrator if you need higher permissions.";
        }

        return $"Access denied: {path}";
    }

    public static bool IsAccessDenied(Exception ex)
    {
        if (ex is UnauthorizedAccessException)
        {
            return true;
        }

        if (ex is not IOException)
        {
            return false;
        }

        var code = ex.HResult & 0xFFFF;
        return code is 5 or 19 or 21 or 32 or 82 or 86 or 1326 or 1331 or 1907 or 1920;
    }

    public static Exception ToScanException(Exception ex, string path, bool elevated)
    {
        if (ex is OperationCanceledException)
        {
            return ex;
        }

        if (IsAccessDenied(ex))
        {
            return new UnauthorizedAccessException(Denied(path, elevated), ex);
        }

        if (ex is DirectoryNotFoundException or FileNotFoundException or IOException)
        {
            return new DirectoryNotFoundException(Missing(path), ex);
        }

        return ex;
    }
}
