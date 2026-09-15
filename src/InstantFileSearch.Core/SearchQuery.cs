using System.Globalization;

namespace InstantFileSearch;

public enum SearchMatchMode
{
    NameOrPath = 0,
    Name = 1,
    Path = 2,
}

public sealed class SearchQuery
{
    public string Text { get; init; } = "";
    public long? MinSizeBytes { get; init; }
    public long? MaxSizeBytes { get; init; }
    public DateTime? ModifiedFrom { get; init; }
    public DateTime? ModifiedTo { get; init; }
    public string? UnderFolder { get; init; }
    public SearchMatchMode Match { get; init; } = SearchMatchMode.NameOrPath;

    public bool HasCriteria =>
        !string.IsNullOrWhiteSpace(Text)
        || MinSizeBytes is not null
        || MaxSizeBytes is not null
        || ModifiedFrom is not null
        || ModifiedTo is not null
        || UnderFolder is not null;

    public static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTime.TryParseExact(
            text.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date.Date
            : null;
    }
}
