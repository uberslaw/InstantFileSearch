using System.Globalization;

namespace InstantFileSearch;

/// <summary>
/// Copy and predicates for the tree/results chrome. WPF binds these strings;
/// tests lock the empty-state and context-menu rules without a Windows UI thread.
/// </summary>
public static class ResultsUi
{
    public const string TreeContext = "tree";

    public static bool IsTreeContext(object? parameter) =>
        parameter is string text && text.Equals(TreeContext, StringComparison.OrdinalIgnoreCase);

    public static bool ShowEmptyState(int itemCount) => itemCount <= 0;

    public static string EmptyState(bool hasScans, bool isSearchActive, int itemCount)
    {
        if (itemCount > 0)
        {
            return "";
        }

        if (!hasScans)
        {
            return "Scan a folder to list files here.";
        }

        if (isSearchActive)
        {
            return "No files match this search.";
        }

        return "This folder is empty.";
    }

    public static string ContentsHeader(bool hasScans, bool isSearchActive, int itemCount)
    {
        if (!hasScans)
        {
            return "CONTENTS";
        }

        if (isSearchActive)
        {
            return itemCount == 1
                ? "RESULTS · 1 file"
                : $"RESULTS · {itemCount.ToString("N0", CultureInfo.InvariantCulture)} files";
        }

        return itemCount == 1
            ? "CONTENTS · 1 item"
            : $"CONTENTS · {itemCount.ToString("N0", CultureInfo.InvariantCulture)} items";
    }

    public static string DetailsMeta(long size, DateTime modified, bool isFolder)
    {
        var kind = isFolder ? "Folder" : "File";
        var sizeText = ByteFormatter.ToString(size);
        if (modified == default || modified == DateTime.MinValue)
        {
            return $"{kind}  ·  {sizeText}";
        }

        return $"{kind}  ·  {sizeText}  ·  {modified.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}";
    }

    public static string? ContextPath(string? entryPath, string? folderPath, bool treeContext) =>
        treeContext ? NullIfEmpty(folderPath) : NullIfEmpty(entryPath) ?? NullIfEmpty(folderPath);

    public static string? ContextName(string? entryName, string? folderName, bool treeContext) =>
        treeContext ? NullIfEmpty(folderName) : NullIfEmpty(entryName) ?? NullIfEmpty(folderName);

    public static string FormatPercent(double percent, bool isRoot)
    {
        if (isRoot)
        {
            return "";
        }

        if (double.IsNaN(percent) || double.IsInfinity(percent) || percent <= 0)
        {
            return "0%";
        }

        if (percent < 1)
        {
            return "<1%";
        }

        return Math.Min(100, percent).ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    public static string FolderGlyph(bool isRoot, ScanLocationKind kind)
    {
        if (!isRoot)
        {
            return "\uE8B7";
        }

        return kind == ScanLocationKind.Network ? "\uE83B" : "\uE7F4";
    }

    public static string FileGlyph(bool isFolder) => isFolder ? "\uE8B7" : "\uE7C3";

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
