using System.Globalization;

namespace InstantFileSearch;

public enum SearchMatchMode
{
    Name = 0,
    Path = 1,
    NameOrPath = 2,
}

public enum SearchScopeKind
{
    CurrentScan = 0,
    AllScans = 1,
    SelectedFolder = 2,
    AllPrevious = 3,
}

/// <summary>
/// How each name/path term matches the Match field. Does not apply to
/// <c>path:</c> filters, Ext, or Regex mode. A term that already has <c>*</c>
/// or <c>?</c> is an explicit glob and is not wrapped.
/// </summary>
public enum SearchTermOperator
{
    Contains = 0,
    IsExactly = 1,
    IsNotExactly = 2,
    BeginsWith = 3,
    EndsWith = 4,
}

public static class SearchScopes
{
    public const string AllScans = "All scans";
    public const string CurrentScan = "Current scan";
    public const string SelectedFolder = "Selected folder";
    public const string AllPrevious = "All previous scans";
    public const string Default = CurrentScan;

    public static SearchScopeKind Parse(string? label) =>
        label switch
        {
            AllScans => SearchScopeKind.AllScans,
            SelectedFolder => SearchScopeKind.SelectedFolder,
            AllPrevious => SearchScopeKind.AllPrevious,
            _ => SearchScopeKind.CurrentScan,
        };
}

public static class SearchMatchModes
{
    public const string Name = "Name";
    public const string Path = "Path";
    public const string NameOrPath = "Name or path";
    public const string Default = Name;
    public const string GroupLabel = "Name / Path";

    public static SearchMatchMode Parse(string? label) =>
        label switch
        {
            Path => SearchMatchMode.Path,
            NameOrPath => SearchMatchMode.NameOrPath,
            _ => SearchMatchMode.Name,
        };
}

public static class SearchTermOperators
{
    public const string Contains = "contains";
    public const string IsExactly = "is exactly";
    public const string IsNotExactly = "is not exactly";
    public const string BeginsWith = "begins with";
    public const string EndsWith = "ends with";
    public const string Default = Contains;

    public static SearchTermOperator Parse(string? label) =>
        label switch
        {
            IsExactly => SearchTermOperator.IsExactly,
            IsNotExactly => SearchTermOperator.IsNotExactly,
            BeginsWith => SearchTermOperator.BeginsWith,
            EndsWith => SearchTermOperator.EndsWith,
            _ => SearchTermOperator.Contains,
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
    public SearchMatchMode Match { get; init; } = SearchMatchMode.Name;
    /// <summary>
    /// How each name/path term is applied. Ignored in Regex mode and for
    /// <c>path:</c> / Ext. Default is contains (substring on the Match field).
    /// </summary>
    public SearchTermOperator TermOperator { get; init; } = SearchTermOperator.Contains;
    /// <summary>
    /// When true, <see cref="Text"/> (minus <c>path:</c> filters) is one .NET
    /// regex. Default is the operator / wildcard / <c>+</c> expression language.
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
        SearchExpression.Parse(Text, UseRegex, TermOperator).HasText
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
