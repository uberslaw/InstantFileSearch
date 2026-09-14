using System.Globalization;
using System.Text;

namespace InstantFileSearch;

public static class ScanLog
{
    public static string DefaultFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "InstantFileSearch",
            "logs",
            "scans.log");

    public static void Append(ScanResult result, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        var path = string.IsNullOrWhiteSpace(filePath) ? DefaultFilePath : filePath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var kind = result.Root.LocationKind == ScanLocationKind.Unknown
            ? ScanLocation.Classify(result.Root.FullPath)
            : result.Root.LocationKind;
        var when = (result.CompletedUtc == default ? DateTime.UtcNow : result.CompletedUtc)
            .ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var line = string.Join('\t',
            when,
            ScanLocation.FormatDuration(result.Duration),
            ScanLocation.KindLabel(kind),
            result.Root.FullPath,
            result.Root.FileCount.ToString("N0", CultureInfo.InvariantCulture) + " files",
            ByteFormatter.ToString(result.Root.Size));

        File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
    }
}
