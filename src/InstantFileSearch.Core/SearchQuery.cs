using System.Globalization;

namespace InstantFileSearch;

public enum SearchMatchMode
{
    NameOrPath = 0,
    Name = 1,
    Path = 2,
}

public enum SearchScopeKind
{
    AllScans = 0,
    CurrentScan = 1,
    SelectedFolder = 2,
}

public static class SearchScopes
{
    public const string AllScans = "All scans";
    public const string CurrentScan = "Current scan";
    public const string SelectedFolder = "Selected folder";

    public static SearchScopeKind Parse(string? label) =>
        label switch
        {
            CurrentScan => SearchScopeKind.CurrentScan,
            SelectedFolder => SearchScopeKind.SelectedFolder,
            _ => SearchScopeKind.AllScans,
        };
}

public sealed class SearchQuery
{
    public string Text { get; init; } = "";
    public string ExtensionsText { get; init; } = "";
    public long? MinSizeBytes { get; init; }
    public long? MaxSizeBytes { get; init; }
    public DateTime? ModifiedFrom { get; init; }
    public DateTime? ModifiedTo { get; init; }
    public string? UnderFolder { get; init; }
    /// <summary>
    /// When set with <see cref="UnderFolder"/>, only items sitting directly in
    /// that folder match — not descendants. Used when the FILES node is selected
    /// with Selected-folder scope (files only; FILES is not a folder hit).
    /// </summary>
    public bool DirectChildrenOnly { get; init; }
    public SearchMatchMode Match { get; init; } = SearchMatchMode.NameOrPath;
    /// <summary>
    /// When true, <see cref="Text"/> (minus <c>path:</c> filters) is one .NET
    /// regex. Default is the exact / wildcard / <c>+</c> expression language.
    /// </summary>
    public bool UseRegex { get; init; }
    /// <summary>
    /// Current-scan scope: the caller passes only that scan's files/folders.
    /// Counts as criteria so an empty box still searches that scan (same idea
    /// as Selected folder setting <see cref="UnderFolder"/>).
    /// </summary>
    public bool RestrictToCurrentScan { get; init; }

    public bool HasInvalidRegex =>
        UseRegex && SearchExpression.Parse(Text, regex: true).IsRegexInvalid;

    public bool HasCriteria =>
        SearchExpression.Parse(Text, UseRegex).HasText
        || ExtensionFilter.HasAny(ExtensionsText)
        || MinSizeBytes is not null
        || MaxSizeBytes is not null
        || ModifiedFrom is not null
        || ModifiedTo is not null
        || UnderFolder is not null
        || RestrictToCurrentScan;

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
