namespace InstantFileSearch;

public enum ScanLocationKind
{
    Unknown = 0,
    Local = 1,
    Network = 2,
}

public static class ScanLocation
{
    public static ScanLocationKind Classify(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return ScanLocationKind.Unknown;
        }

        var trimmed = fullPath.Trim();
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return ScanLocationKind.Network;
        }

        if (!LocalPathGuard.TryGetFullPath(trimmed, out var path))
        {
            return ScanLocationKind.Unknown;
        }

        try
        {
            var volume = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(volume))
            {
                return ScanLocationKind.Unknown;
            }

            var drive = new DriveInfo(volume);
            return drive.DriveType == DriveType.Network
                ? ScanLocationKind.Network
                : ScanLocationKind.Local;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return ScanLocationKind.Unknown;
        }
    }

    public static string KindLabel(ScanLocationKind kind) => kind switch
    {
        ScanLocationKind.Network => "network",
        ScanLocationKind.Local => "this PC",
        _ => "unknown",
    };

    public static string DisplayName(string fullPath, ScanLocationKind kind)
    {
        if (!LocalPathGuard.TryGetFullPath(fullPath, out var path))
        {
            path = fullPath.Trim();
        }

        return $"{path} ({KindLabel(kind)})";
    }

    public static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        if (duration.TotalHours >= 1)
        {
            return duration.TotalHours.ToString("0.0") + " h";
        }

        if (duration.TotalMinutes >= 1)
        {
            return duration.TotalMinutes.ToString("0.0") + " min";
        }

        if (duration.TotalSeconds >= 10)
        {
            return duration.TotalSeconds.ToString("0.0") + " s";
        }

        if (duration.TotalSeconds >= 1)
        {
            return duration.TotalSeconds.ToString("0.00") + " s";
        }

        return Math.Max(1, duration.TotalMilliseconds).ToString("0") + " ms";
    }
}
