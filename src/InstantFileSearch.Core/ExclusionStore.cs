using System.Text;
using System.Text.Json;

namespace InstantFileSearch;

public static class ExclusionStore
{
    public static string DefaultFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "InstantFileSearch",
            "exclusions.bin");

    public static FolderExclusionSet Load(string filePath, IByteProtector? protector = null)
    {
        protector ??= ByteProtector.CreateDefault();
        if (!ProtectedFile.TryReadAll(filePath, protector, out var bytes))
        {
            return new FolderExclusionSet();
        }

        try
        {
            var paths = JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(bytes));
            return FolderExclusionSet.From(paths);
        }
        catch (JsonException)
        {
            return new FolderExclusionSet();
        }
    }

    public static void Save(FolderExclusionSet set, string filePath, IByteProtector? protector = null)
    {
        ArgumentNullException.ThrowIfNull(set);
        protector ??= ByteProtector.CreateDefault();
        var json = JsonSerializer.Serialize(set.Items);
        ProtectedFile.WriteAll(filePath, Encoding.UTF8.GetBytes(json), protector);
    }
}
