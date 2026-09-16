using System.Globalization;

namespace InstantFileSearch;

/// <summary>
/// Copy, Explorer, and predicates for the tree/results chrome. WPF binds these strings;
/// tests lock the empty-state and context-menu rules without a Windows UI thread.
/// </summary>
public static class ResultsUi
{
    public const string TreeContext = "tree";

    public static bool IsTreeContext(object? parameter) =>
        parameter is string text && text.Equals(TreeContext, StringComparison.OrdinalIgnoreCase);

    public static bool ShowEmptyState(int itemCount) => itemCount <= 0;

    public static string EmptyState(bool hasScans, bool isSearchActive, int itemCount, bool filesAtLevel = false)
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

        if (filesAtLevel)
        {
            return "No files at this level.";
        }

        return "This folder is empty.";
    }

    public static string ContentsHeader(bool hasScans, bool isSearchActive, int itemCount) =>
        ContentsHeader(hasScans, isSearchActive, itemCount, filesAtLevel: false, folderName: null);

    public static string ContentsHeader(
        bool hasScans,
        bool isSearchActive,
        int itemCount,
        bool filesAtLevel,
        string? folderName)
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

        if (filesAtLevel)
        {
            var where = string.IsNullOrWhiteSpace(folderName) ? "this folder" : folderName;
            return itemCount == 1
                ? $"Files in {where} · 1 file"
                : $"Files in {where} · {itemCount.ToString("N0", CultureInfo.InvariantCulture)} files";
        }

        return itemCount == 1
            ? "CONTENTS · 1 item"
            : $"CONTENTS · {itemCount.ToString("N0", CultureInfo.InvariantCulture)} items";
    }

    public static string DetailsMeta(long size, DateTime modified, bool isFolder, bool isFilesNode = false)
    {
        var kind = isFilesNode ? "Files" : isFolder ? "Folder" : "File";
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

    /// <summary>
    /// Folder Explorer should open for a left-tree node. FILES is not a
    /// filesystem path — use the parent folder. Never throws on null/empty.
    /// </summary>
    public static string? TreeExplorerPath(FolderNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node.IsFilesNode)
        {
            return NullIfEmpty(node.Parent?.FullPath) ?? NullIfEmpty(node.FullPath);
        }

        return NullIfEmpty(node.FullPath);
    }

    /// <summary>
    /// <c>explorer.exe</c> arguments: open a folder, or <c>/select</c> a file.
    /// UNC paths are passed through quoted; do not convert them to local paths.
    /// </summary>
    public static string ExplorerArguments(string path, bool isFolder) =>
        isFolder ? $"\"{path}\"" : $"/select,\"{path}\"";

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

    public static string FolderGlyph(bool isRoot, ScanLocationKind kind, bool isFilesNode = false)
    {
        if (isFilesNode)
        {
            return FilesGlyph();
        }

        if (!isRoot)
        {
            return "\uE8B7";
        }

        return kind == ScanLocationKind.Network ? "\uE83B" : "\uE7F4";
    }

    /// <summary>Segoe MDL2 Copy — stacked pages, distinct from folder and drive.</summary>
    public static string FilesGlyph() => "\uE8C8";

    public static string FileGlyph(bool isFolder) => isFolder ? "\uE8B7" : "\uE7C3";

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
