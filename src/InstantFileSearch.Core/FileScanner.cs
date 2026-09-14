using System.Diagnostics;

namespace InstantFileSearch;

public sealed class FileScanner
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false,
    };

    public ScanResult Scan(
        string rootPath,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("A folder path is required.", nameof(rootPath));
        }

        var fullRoot = Path.GetFullPath(rootPath);
        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException($"Folder not found: {fullRoot}");
        }

        var clock = Stopwatch.StartNew();
        var files = new List<FileEntry>();
        var errorCount = 0;
        var state = new ScanState();

        var root = ScanDirectory(
            new DirectoryInfo(fullRoot),
            parent: null,
            files,
            ref errorCount,
            state,
            progress,
            cancellationToken);

        SortTree(root);
        clock.Stop();

        return new ScanResult
        {
            Root = root,
            AllFiles = files,
            Duration = clock.Elapsed,
            ErrorCount = errorCount,
        };
    }

    private static FolderNode ScanDirectory(
        DirectoryInfo directory,
        FolderNode? parent,
        List<FileEntry> allFiles,
        ref int errorCount,
        ScanState state,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var node = new FolderNode
        {
            Name = parent is null ? directory.FullName : directory.Name,
            FullPath = directory.FullName,
            Parent = parent,
            Modified = SafeTimestamp(directory),
        };

        state.Folders++;
        Report(progress, state, directory.FullName);

        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = directory.EnumerateFileSystemInfos("*", Options);
        }
        catch (Exception ex) when (IsSkippable(ex))
        {
            errorCount++;
            return node;
        }

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (entry is FileInfo file)
                {
                    var item = new FileEntry
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        Size = SafeLength(file),
                        Modified = SafeTimestamp(file),
                        Parent = node,
                    };
                    node.Files.Add(item);
                    node.Size += item.Size;
                    node.FileCount++;
                    allFiles.Add(item);
                    state.Files++;
                    state.Bytes += item.Size;
                    if (state.Files % 250 == 0)
                    {
                        Report(progress, state, file.FullName);
                    }
                }
                else if (entry is DirectoryInfo childDir)
                {
                    var child = ScanDirectory(
                        childDir,
                        node,
                        allFiles,
                        ref errorCount,
                        state,
                        progress,
                        cancellationToken);
                    node.Folders.Add(child);
                    node.Size += child.Size;
                    node.FileCount += child.FileCount;
                    node.FolderCount += 1 + child.FolderCount;
                    if (child.Modified > node.Modified)
                    {
                        node.Modified = child.Modified;
                    }
                }
            }
            catch (Exception ex) when (IsSkippable(ex))
            {
                errorCount++;
            }
        }

        return node;
    }

    private static void SortTree(FolderNode node)
    {
        node.Folders.Sort((a, b) => b.Size.CompareTo(a.Size));
        node.Files.Sort((a, b) => b.Size.CompareTo(a.Size));
        foreach (var child in node.Folders)
        {
            SortTree(child);
        }
    }

    private static void Report(IProgress<ScanProgress>? progress, ScanState state, string path)
    {
        progress?.Report(new ScanProgress(state.Files, state.Folders, state.Bytes, path));
    }

    private static long SafeLength(FileInfo file)
    {
        try
        {
            return file.Length;
        }
        catch
        {
            return 0;
        }
    }

    private static DateTime SafeTimestamp(FileSystemInfo info)
    {
        try
        {
            return info.LastWriteTime;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static bool IsSkippable(Exception ex) =>
        ex is UnauthorizedAccessException
            or DirectoryNotFoundException
            or FileNotFoundException
            or IOException
            or System.Security.SecurityException;

    private sealed class ScanState
    {
        public int Files;
        public int Folders;
        public long Bytes;
    }
}
