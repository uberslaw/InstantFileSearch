using System.Globalization;

namespace InstantFileSearch;

/// <summary>
/// Copy, Explorer, and predicates for the tree/results chrome. WPF binds these strings;
/// tests lock the empty-state and context-menu rules without a Windows UI thread.
/// </summary>
public static class ResultsUi
{
    public const string TreeContext = "tree";
    public const string RemoveFromListHeader = "Remove from list";
    public const string RemoveFromListTip =
        "Removes this scan from the list. Does not delete files on disk.";

    public static bool IsTreeContext(object? parameter) =>
        parameter is string text && text.Equals(TreeContext, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Tree context "Remove from list" is only for a scan root. Nested folders
    /// and FILES must not offer a command that drops the parent location.
    /// </summary>
    public static bool ShowRemoveFromList(FolderNode? node, bool isScanning = false) =>
        !isScanning && FolderFilesNode.IsScanRoot(node);

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
            return "Nothing matches this search.";
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
                ? "RESULTS · 1 item"
                : $"RESULTS · {itemCount.ToString("N0", CultureInfo.InvariantCulture)} items";
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

    /// <summary>
    /// Select a folder result to highlight it in the left tree. Open / double-click
    /// shows that folder’s contents and clears the search box (Advanced filters stay
    /// in the boxes until you change them).
    /// </summary>
    public const string FolderResultClickHint =
        "Select a folder result to highlight it in the left tree. Open or double-click to show its contents (clears the search box).";

    public const string SearchBoxPlaceholder =
        "Search  ·  exact name  ·  * ?  ·  + AND  ·  path:";

    /// <summary>
    /// Full expression language for the search box tooltip. Keep in sync with README.
    /// </summary>
    public const string SearchBoxTip =
        "Single term: exact whole name, including extension (cmd does not match cmd.exe or anythingwithcmdinit).\n" +
        "Wildcards: * any characters, ? one character (cmd*, *cmd, *cmd*, c?d).\n" +
        "AND: cisco + zero (spaces around + optional, cisco+zero is the same). Each term must match. A term without * or ? is treated as contains (*term*) only when + is used. A single term without + stays exact.\n" +
        "Quoted phrase: \"cisco zero\" is one term (exact if alone; contains if used with +).\n" +
        "path:Incoming — hits whose full path contains Incoming (/ and \\ are the same). Applies even when Match is Name. No wildcards → contains; path:*\\share\\* uses * and ?. Several path: tokens AND together.\n" +
        "Match Name / Path / Name or path applies to terms, not to path:. Scope, size, and date still apply. Empty box + Advanced filters is allowed.\n" +
        "Spaces are not AND. No OR. No regex. Ctrl+F or F3.";

    public const string SearchMatchTip =
        "Match applies to search terms, not to path: filters. Without * or ?, a single term equals the whole name (or the whole path). cisco + zero is contains-AND on this field. Use *\\cmd or *cmd* for partial paths.";

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
