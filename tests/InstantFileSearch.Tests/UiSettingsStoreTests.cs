using InstantFileSearch;

namespace InstantFileSearch.Tests;

public class UiSettingsStoreTests
{
    [Fact]
    public void RoundtripPersistsTreeSortAndUnknownFallsBack()
    {
        var path = Path.Combine(Path.GetTempPath(), "ifs-ui-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Assert.Equal(TreeSort.Default, UiSettingsStore.Load(path).TreeSort);

            UiSettingsStore.Save(new UiSettings { TreeSort = TreeSortMode.NameAscending }, path);
            Assert.Equal(TreeSortMode.NameAscending, UiSettingsStore.Load(path).TreeSort);
            Assert.Contains("A–Z", File.ReadAllText(path), StringComparison.Ordinal);

            File.WriteAllText(path, """{"TreeSort":"not-a-sort"}""");
            Assert.Equal(TreeSort.Default, UiSettingsStore.Load(path).TreeSort);

            File.WriteAllText(path, "not-json");
            Assert.Equal(TreeSort.Default, UiSettingsStore.Load(path).TreeSort);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
