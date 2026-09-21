namespace InstantFileSearch;

/// <summary>
/// Merge/move planned from the in-memory scan index. Does not walk the
/// filesystem for a prepare/ETA step (that is the Explorer hang).
/// </summary>
public static class IndexedFileMerge
{
    public const string DestNeedsScanMessage =
        "That destination is not in the scan index. Scan it first, then merge. IFS will not walk it the Explorer way.";

    public static string ProgressText(int moved, int total, int skipped, int failed) =>
        $"Moved {moved}/{total}, skipped {skipped}, failed {failed}";

    public static bool TryFindFolder(
        IEnumerable<ScanResult> scans,
        string? path,
        out FolderNode? folder,
        out ScanResult? scan)
    {
        folder = null;
        scan = null;
        if (scans is null || !IndexedPath.TryCanonicalize(path, out var want))
        {
            return false;
        }

        foreach (var item in scans)
        {
            if (FindFolder(item.Root, want) is { } match)
            {
                folder = match;
                scan = item;
                return true;
            }
        }

        return false;
    }

    public static ScanResult? FindScan(IEnumerable<ScanResult> scans, FolderNode? node)
    {
        var root = node;
        while (root?.Parent is not null)
        {
            root = root.Parent;
        }

        if (root is null || scans is null)
        {
            return null;
        }

        return scans.FirstOrDefault(scan => ReferenceEquals(scan.Root, root));
    }

    public static MergePlanResult TryBuildPlan(
        IReadOnlyList<ScanResult> scans,
        FolderNode source,
        string destPath,
        CollisionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(scans);
        ArgumentNullException.ThrowIfNull(source);
        if (!IndexedPath.TryCanonicalize(destPath, out _))
        {
            return MergePlanResult.Fail("That destination path is invalid.");
        }

        if (!TryFindFolder(scans, destPath, out var dest, out _) || dest is null)
        {
            return MergePlanResult.Fail(DestNeedsScanMessage, destNeedsScan: true);
        }

        return TryBuildPlan(scans, source, dest, policy);
    }

    public static MergePlanResult TryBuildPlan(
        IReadOnlyList<ScanResult> scans,
        FolderNode source,
        FolderNode dest,
        CollisionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(scans);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dest);

        if (source.IsFilesNode)
        {
            return MergePlanResult.Fail("FILES is not a folder you can merge from.");
        }

        if (dest.IsFilesNode)
        {
            return MergePlanResult.Fail("FILES is not a folder you can merge into.");
        }

        if (FindScan(scans, source) is not { } sourceScan)
        {
            return MergePlanResult.Fail("The source folder is not in the scan index.");
        }

        if (FindScan(scans, dest) is not { } destScan)
        {
            return MergePlanResult.Fail(DestNeedsScanMessage, destNeedsScan: true);
        }

        if (ReferenceEquals(source, dest)
            || (IndexedPath.TryCanonicalize(source.FullPath, out var sourcePath)
                && IndexedPath.TryCanonicalize(dest.FullPath, out var destFull)
                && sourcePath.Equals(destFull, LocalPathGuard.Comparison)))
        {
            return MergePlanResult.Fail("Choose a different destination folder.");
        }

        if (IsAncestorOf(source, dest) || IndexedPath.IsSameOrUnder(dest.FullPath, source.FullPath))
        {
            return MergePlanResult.Fail("Cannot merge a folder into itself or into a folder inside it.");
        }

        var files = new List<FileEntry>();
        CollectFiles(source, files);
        if (files.Count == 0)
        {
            return MergePlanResult.Fail("No files to merge in that folder.");
        }

        var items = new List<MergeItem>(files.Count);
        var collisions = 0;
        var transferBytes = 0L;
        var transferCount = 0;
        foreach (var file in files)
        {
            if (!TryRelative(file, source, out var relative))
            {
                return MergePlanResult.Fail("Could not compute a relative path from the index for " + file.Name + ".");
            }

            var destPath = IndexedPath.Combine(dest.FullPath, relative);
            var collision = FindFileAt(dest, relative) is not null
                || DestPathIndexed(destScan, destPath);
            if (collision)
            {
                collisions++;
            }

            var transfer = !collision || policy == CollisionPolicy.Overwrite;
            if (transfer)
            {
                transferCount++;
                transferBytes += file.Size;
            }

            items.Add(new MergeItem
            {
                File = file,
                SourcePath = file.FullPath,
                DestPath = destPath,
                RelativePath = relative,
                IndexCollision = collision,
                WillTransfer = transfer,
            });
        }

        var sameVolume = VolumeRoot.AreSame(source.FullPath, dest.FullPath);
        return MergePlanResult.Ok(new MergePlan
        {
            SourceFolder = source,
            DestFolder = dest,
            SourceScan = sourceScan,
            DestScan = destScan,
            Items = items,
            Policy = policy,
            SameVolume = sameVolume,
            CollisionCount = collisions,
            TransferCount = transferCount,
            CopyBytes = sameVolume ? 0 : transferBytes,
        });
    }

    public static string ConfirmMessage(MergePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var verb = plan.SameVolume ? "move/rename" : "copy";
        var lines = new List<string>
        {
            $"Merge '{plan.SourceFolder.Name}' into '{plan.DestFolder.Name}'?",
            "",
            plan.SourceFolder.FullPath,
            "→",
            plan.DestFolder.FullPath,
            "",
            plan.SameVolume
                ? $"{plan.FileCount} files to {verb}"
                : plan.TransferCount > 0
                    ? $"{plan.TransferCount} files to {verb} ({ByteFormatter.ToString(plan.CopyBytes)})"
                    : $"{plan.FileCount} files to {verb}",
        };

        if (plan.CollisionCount > 0)
        {
            lines.Add(plan.Policy == CollisionPolicy.Overwrite
                ? $"{plan.CollisionCount} collisions will be overwritten"
                : $"{plan.CollisionCount} already exist at the destination (skip)");
        }

        lines.Add(plan.SameVolume
            ? "Same volume — rename, not a copy. Explorer is not used."
            : "Cross-volume — copy then delete from the source. Explorer is not used.");
        lines.Add(plan.Policy == CollisionPolicy.Overwrite
            ? "Existing destination files will be overwritten."
            : "Existing destination files will be skipped.");
        return string.Join('\n', lines);
    }

    public static MergeExecuteResult Execute(
        MergePlan plan,
        IFileMover mover,
        IProgress<MergeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(mover);

        var sourceScan = plan.SourceScan;
        var destScan = plan.DestScan;
        var moved = 0;
        var skipped = 0;
        var failed = 0;
        var cancelled = false;

        foreach (var item in plan.Items)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            Report(progress, moved, skipped, failed, plan.FileCount, item.SourcePath);

            try
            {
                if (plan.Policy == CollisionPolicy.SkipExisting && item.IndexCollision)
                {
                    skipped++;
                    continue;
                }

                var exists = mover.FileExists(item.DestPath);
                if (exists && plan.Policy == CollisionPolicy.SkipExisting)
                {
                    skipped++;
                    continue;
                }

                if (!IndexedPath.TryGetParent(item.DestPath, out var destDir))
                {
                    failed++;
                    continue;
                }

                mover.EnsureDirectory(destDir);
                var overwrite = plan.Policy == CollisionPolicy.Overwrite || exists;
                if (plan.SameVolume)
                {
                    mover.Move(item.SourcePath, item.DestPath, overwrite);
                }
                else
                {
                    mover.CopyThenDelete(item.SourcePath, item.DestPath, overwrite);
                }

                ApplyIndexMove(plan, item, overwrite || item.IndexCollision, ref sourceScan, ref destScan);
                moved++;
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                break;
            }
            catch (Exception ex) when (
                ex is IOException
                    or UnauthorizedAccessException
                    or NotSupportedException
                    or ArgumentException)
            {
                failed++;
            }
        }

        Report(progress, moved, skipped, failed, plan.FileCount, "");
        return new MergeExecuteResult
        {
            Moved = moved,
            Skipped = skipped,
            Failed = failed,
            Total = plan.FileCount,
            Cancelled = cancelled,
            SourceScan = sourceScan,
            DestScan = destScan,
        };
    }

    private static void ApplyIndexMove(
        MergePlan plan,
        MergeItem item,
        bool replaceDest,
        ref ScanResult sourceScan,
        ref ScanResult destScan)
    {
        if (!IndexedFileDelete.TryRemoveFromIndex(sourceScan, item.SourcePath, out var afterSource))
        {
            var remaining = sourceScan.AllFiles
                .Where(file => !SamePath(file.FullPath, item.SourcePath))
                .ToList();
            afterSource = WithFiles(sourceScan, remaining);
            RemoveFromParentList(item.File);
        }

        sourceScan = afterSource;
        if (plan.SameScan)
        {
            destScan = afterSource;
        }

        if (replaceDest && IndexedFileDelete.IsIndexedFile(destScan, item.DestPath, out _))
        {
            IndexedFileDelete.TryRemoveFromIndex(destScan, item.DestPath, out destScan);
            if (plan.SameScan)
            {
                sourceScan = destScan;
            }
        }
        else if (replaceDest)
        {
            RemoveIndexedDestByPath(destScan, item.DestPath);
        }

        var destParent = EnsureFolderChain(plan.DestFolder, IndexedPath.RelativeDirectory(item.RelativePath));
        var moved = new FileEntry
        {
            Name = item.File.Name,
            FullPath = item.DestPath,
            Size = item.File.Size,
            Modified = item.File.Modified,
            Parent = destParent,
        };
        destParent.Files.Add(moved);
        for (var walk = destParent; walk is not null; walk = walk.Parent)
        {
            walk.Size += moved.Size;
            walk.FileCount++;
        }

        FolderFilesNode.Attach(destScan.Root);
        if (!plan.SameScan)
        {
            FolderFilesNode.Attach(sourceScan.Root);
        }

        destScan = WithFiles(destScan, CollectFiles(destScan.Root));
        if (plan.SameScan)
        {
            sourceScan = destScan;
        }
        else
        {
            sourceScan = WithFiles(sourceScan, CollectFiles(sourceScan.Root));
        }
    }

    private static void RemoveFromParentList(FileEntry file)
    {
        var parent = file.Parent;
        if (parent is null)
        {
            return;
        }

        if (!parent.Files.Remove(file))
        {
            var match = parent.Files.Find(item => SamePath(item.FullPath, file.FullPath));
            if (match is not null)
            {
                parent.Files.Remove(match);
            }
        }

        var size = file.Size;
        for (var walk = parent; walk is not null; walk = walk.Parent)
        {
            walk.Size = Math.Max(0, walk.Size - size);
            walk.FileCount = Math.Max(0, walk.FileCount - 1);
        }
    }

    private static void RemoveIndexedDestByPath(ScanResult scan, string destPath)
    {
        foreach (var file in scan.AllFiles)
        {
            if (SamePath(file.FullPath, destPath))
            {
                RemoveFromParentList(file);
                return;
            }
        }
    }

    private static FolderNode EnsureFolderChain(FolderNode dest, string relativeDir)
    {
        if (string.IsNullOrWhiteSpace(relativeDir))
        {
            return dest;
        }

        var current = dest;
        foreach (var segment in IndexedPath.SplitRelative(relativeDir))
        {
            var child = current.Folders.Find(folder =>
                folder.Name.Equals(segment, LocalPathGuard.Comparison));
            if (child is null)
            {
                child = new FolderNode
                {
                    Name = segment,
                    FullPath = IndexedPath.Combine(current.FullPath, segment),
                    Parent = current,
                    LocationKind = ScanLocationKind.Unknown,
                };
                current.Folders.Add(child);
                for (var walk = current; walk is not null; walk = walk.Parent)
                {
                    walk.FolderCount++;
                }
            }

            current = child;
        }

        return current;
    }

    private static bool TryRelative(FileEntry file, FolderNode source, out string relative)
    {
        var parts = new List<string> { file.Name };
        var walk = file.Parent;
        while (walk is not null && !ReferenceEquals(walk, source))
        {
            if (!walk.IsFilesNode)
            {
                parts.Add(walk.Name);
            }

            walk = walk.Parent;
        }

        if (ReferenceEquals(walk, source))
        {
            parts.Reverse();
            relative = string.Join(IndexedPath.Separator(source.FullPath), parts);
            return relative.Length > 0;
        }

        return IndexedPath.TryGetRelative(file.FullPath, source.FullPath, out relative);
    }

    private static void CollectFiles(FolderNode folder, List<FileEntry> files)
    {
        files.AddRange(folder.Files);
        foreach (var child in folder.Folders)
        {
            CollectFiles(child, files);
        }
    }

    private static List<FileEntry> CollectFiles(FolderNode folder)
    {
        var files = new List<FileEntry>();
        CollectFiles(folder, files);
        return files;
    }

    private static FileEntry? FindFileAt(FolderNode dest, string relative)
    {
        var parts = IndexedPath.SplitRelative(relative);
        if (parts.Length == 0)
        {
            return null;
        }

        var folder = dest;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var segment = parts[i];
            folder = folder.Folders.Find(child => child.Name.Equals(segment, LocalPathGuard.Comparison));
            if (folder is null)
            {
                return null;
            }
        }

        var name = parts[^1];
        return folder.Files.Find(file => file.Name.Equals(name, LocalPathGuard.Comparison));
    }

    private static FolderNode? FindFolder(FolderNode node, string canonicalPath)
    {
        if (node.IsFilesNode)
        {
            return null;
        }

        if (IndexedPath.TryCanonicalize(node.FullPath, out var path)
            && path.Equals(canonicalPath, LocalPathGuard.Comparison))
        {
            return node;
        }

        foreach (var child in node.Folders)
        {
            if (FindFolder(child, canonicalPath) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    private static bool DestPathIndexed(ScanResult scan, string destPath) =>
        scan.AllFiles.Any(file => SamePath(file.FullPath, destPath));

    private static bool SamePath(string left, string right)
    {
        if (IndexedPath.TryCanonicalize(left, out var a)
            && IndexedPath.TryCanonicalize(right, out var b))
        {
            return a.Equals(b, LocalPathGuard.Comparison);
        }

        return left.Equals(right, LocalPathGuard.Comparison);
    }

    private static bool IsAncestorOf(FolderNode ancestor, FolderNode node)
    {
        for (var walk = node.Parent; walk is not null; walk = walk.Parent)
        {
            if (ReferenceEquals(walk, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static ScanResult WithFiles(ScanResult scan, IReadOnlyList<FileEntry> files) => new()
    {
        Root = scan.Root,
        AllFiles = files,
        Duration = scan.Duration,
        ErrorCount = scan.ErrorCount,
        CompletedUtc = scan.CompletedUtc,
    };

    private static void Report(
        IProgress<MergeProgress>? progress,
        int moved,
        int skipped,
        int failed,
        int total,
        string current)
    {
        progress?.Report(new MergeProgress
        {
            Moved = moved,
            Skipped = skipped,
            Failed = failed,
            Total = total,
            CurrentPath = current,
        });
    }
}
