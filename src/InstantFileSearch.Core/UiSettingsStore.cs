using System.Text.Json;

namespace InstantFileSearch;

public sealed class UiSettings
{
    public TreeSortMode TreeSort { get; init; } = TreeSort.Default;
}

/// <summary>
/// Plain JSON next to the scan cache. Tree sort is not secret, so this is not DPAPI-wrapped.
/// </summary>
public static class UiSettingsStore
{
    public static string DefaultFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "InstantFileSearch",
            "ui-settings.json");

    public static UiSettings Load(string? filePath = null)
    {
        var path = string.IsNullOrWhiteSpace(filePath) ? DefaultFilePath : filePath;
        if (!File.Exists(path))
        {
            return new UiSettings();
        }

        try
        {
            var json = File.ReadAllText(path);
            var document = JsonSerializer.Deserialize<Document>(json);
            return new UiSettings
            {
                TreeSort = TreeSort.Parse(document?.TreeSort),
            };
        }
        catch (Exception ex) when (
            ex is JsonException
                or IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            return new UiSettings();
        }
    }

    public static void Save(UiSettings settings, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var path = string.IsNullOrWhiteSpace(filePath) ? DefaultFilePath : filePath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(new Document
        {
            TreeSort = TreeSort.Label(settings.TreeSort),
        });
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    private sealed class Document
    {
        public string? TreeSort { get; set; }
    }
}
