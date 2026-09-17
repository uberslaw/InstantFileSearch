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
        CancellationToken cancellationToken = default,
        IEnumerable<string>? excludeDirectories = null)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("A folder path is required.", nameof(rootPath));
        }

        if (!LocalPathGuard.TryGetFullPath(rootPath, out var fullRoot))
        {
            throw new ArgumentException("The folder path is invalid.", nameof(rootPath));
        }

        var exclusions = FolderExclusionSet.From(excludeDirectories);
        var location = ScanLocation.Classify(fullRoot);
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
            cancellationToken,
            exclusions,
            location,
            fullRoot);

        SortTree(root);
        FolderFilesNode.Attach(root);
        clock.Stop();
        root.ScanDuration = clock.Elapsed;
        root.LocationKind = location;

        return new ScanResult
        {
            Root = root,
            AllFiles = files,
            Duration = clock.Elapsed,
            ErrorCount = errorCount,
            CompletedUtc = DateTime.UtcNow,
        };
    }

    private static FolderNode ScanDirectory(
        DirectoryInfo directory,
        FolderNode? parent,
        List<FileEntry> allFiles,
        ref int errorCount,
        ScanState state,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken,
        FolderExclusionSet exclusions,
        ScanLocationKind rootLocation,
        string rootFullPath)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var node = new FolderNode
        {
            Name = parent is null
                ? ScanLocation.DisplayName(rootFullPath, rootLocation)
                : directory.Name,
            FullPath = parent is null ? rootFullPath : directory.FullName,
            Parent = parent,
            Modified = SafeTimestamp(directory),
            LocationKind = parent is null ? rootLocation : ScanLocationKind.Unknown,
        };

        state.Folders++;
        Report(progress, state, node.FullPath);

        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = directory.EnumerateFileSystemInfos("*", Options);
        }
        catch (Exception ex) when (IsSkippable(ex))
        {
            return SkipOrThrowRoot(ex, parent, parent is null ? rootFullPath : directory.FullName, node, ref errorCount);
        }

        try
        {
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
                        if (exclusions.Contains(childDir.FullName))
                        {
                            continue;
                        }

                        var child = ScanDirectory(
                            childDir,
                            node,
                            allFiles,
                            ref errorCount,
                            state,
                            progress,
                            cancellationToken,
                            exclusions,
                            rootLocation,
                            rootFullPath);
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
        }
        catch (Exception ex) when (IsSkippable(ex))
        {
            return SkipOrThrowRoot(ex, parent, parent is null ? rootFullPath : directory.FullName, node, ref errorCount);
        }

        return node;
    }

    private static FolderNode SkipOrThrowRoot(
        Exception ex,
        FolderNode? parent,
        string path,
        FolderNode node,
        ref int errorCount)
    {
        if (parent is null)
        {
            throw ScanAccess.ToScanException(
                ex,
                path,
                ProcessElevation.IsCurrentProcessElevated());
        }

        errorCount++;
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
