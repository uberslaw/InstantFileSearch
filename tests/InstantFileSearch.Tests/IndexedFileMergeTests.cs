using InstantFileSearch;

namespace InstantFileSearch.Tests;

public class IndexedFileMergeTests
{
    [Fact]
    public void PlanCountsSkipAndOverwriteForOverlappingSecondLevelFolder()
    {
        var tree = OverlappingTree();
        var skip = IndexedFileMerge.TryBuildPlan(
            [tree.Scan],
            tree.Compressed,
            tree.AnchorSpan,
            CollisionPolicy.SkipExisting);
        Assert.NotNull(skip.Plan);
        Assert.Null(skip.Error);
        Assert.Equal(3, skip.Plan!.FileCount);
        Assert.Equal(1, skip.Plan.CollisionCount);
        Assert.Equal(2, skip.Plan.TransferCount);
        Assert.True(skip.Plan.SameVolume);
        Assert.Equal(0, skip.Plan.CopyBytes);
        Assert.Contains(skip.Plan.Items, item => item.RelativePath.Replace('/', '\\').Equals(@"Foo\a.txt", StringComparison.OrdinalIgnoreCase) && item.IndexCollision && !item.WillTransfer);
        Assert.Contains(skip.Plan.Items, item => item.RelativePath.Replace('/', '\\').Equals(@"Foo\b.txt", StringComparison.OrdinalIgnoreCase) && item.WillTransfer);
        Assert.Contains("skip", IndexedFileMerge.ConfirmMessage(skip.Plan), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rename", IndexedFileMerge.ConfirmMessage(skip.Plan), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("23 hour", IndexedFileMerge.ConfirmMessage(skip.Plan), StringComparison.OrdinalIgnoreCase);

        var overwrite = IndexedFileMerge.TryBuildPlan(
            [tree.Scan],
            tree.Compressed,
            tree.AnchorSpan,
            CollisionPolicy.Overwrite);
        Assert.Equal(3, overwrite.Plan!.FileCount);
        Assert.Equal(1, overwrite.Plan.CollisionCount);
        Assert.Equal(3, overwrite.Plan.TransferCount);
        Assert.All(overwrite.Plan.Items, item => Assert.True(item.WillTransfer));
        Assert.Contains("overwritten", IndexedFileMerge.ConfirmMessage(overwrite.Plan), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SameVolumeCallsMoveNotCopy()
    {
        var tree = OverlappingTree();
        var plan = IndexedFileMerge.TryBuildPlan(
            [tree.Scan],
            tree.Compressed,
            tree.AnchorSpan,
            CollisionPolicy.SkipExisting).Plan!;
        var mover = new RecordingFileMover();

        var result = IndexedFileMerge.Execute(plan, mover);

        Assert.Equal(2, result.Moved);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Failed);
        Assert.Equal(2, mover.Moves.Count);
        Assert.Empty(mover.Copies);
        Assert.Contains(mover.Moves, pair => pair.Dest.EndsWith(@"Foo\b.txt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(mover.Moves, pair => pair.Dest.EndsWith(@"Foo\a.txt", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Moved 2/3, skipped 1, failed 0", result.StatusText);
    }

    [Fact]
    public void CrossVolumeCallsCopyThenDelete()
    {
        var source = Folder("compressed", @"C:\Incoming\compressed");
        var destRoot = Folder("backup", @"D:\backup");
        var dest = Folder("Anchor Span", @"D:\backup\Anchor Span", destRoot);
        destRoot.Folders.Add(dest);
        var zip = AddFile(source, "big.zip", @"C:\Incoming\compressed\big.zip", 15_600_000_000);
        source.Size = zip.Size;
        source.FileCount = 1;
        destRoot.FolderCount = 1;
        FolderFilesNode.Attach(source);
        FolderFilesNode.Attach(destRoot);
        var sourceScan = new ScanResult { Root = source, AllFiles = [zip] };
        var destScan = new ScanResult { Root = destRoot, AllFiles = [] };

        var built = IndexedFileMerge.TryBuildPlan(
            [sourceScan, destScan],
            source,
            dest,
            CollisionPolicy.SkipExisting);
        Assert.False(built.Plan!.SameVolume);
        Assert.Equal(zip.Size, built.Plan.CopyBytes);
        Assert.Contains("copy", IndexedFileMerge.ConfirmMessage(built.Plan), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ByteFormatter.ToString(zip.Size), IndexedFileMerge.ConfirmMessage(built.Plan), StringComparison.Ordinal);

        var mover = new RecordingFileMover();
        var result = IndexedFileMerge.Execute(built.Plan, mover);
        Assert.Equal(1, result.Moved);
        Assert.Empty(mover.Moves);
        Assert.Equal([(zip.FullPath, @"D:\backup\Anchor Span\big.zip", false)], mover.Copies);
    }

    [Fact]
    public void SkipLeavesSourceFileAndDestOriginal()
    {
        var tree = OverlappingTree();
        var destOriginal = tree.DestExisting;
        var plan = IndexedFileMerge.TryBuildPlan(
            [tree.Scan],
            tree.Compressed,
            tree.AnchorSpan,
            CollisionPolicy.SkipExisting).Plan!;
        var mover = new RecordingFileMover();
        mover.Existing.Add(destOriginal.FullPath);

        var result = IndexedFileMerge.Execute(plan, mover);
        var updated = result.DestScan;

        Assert.Contains(updated.AllFiles, file => file.Name == "a.txt" && Same(file.FullPath, destOriginal.FullPath));
        Assert.Contains(result.SourceScan.AllFiles, file => file.Name == "a.txt" && Same(file.FullPath, tree.SourceCollision.FullPath));
        Assert.Contains(updated.AllFiles, file => file.Name == "b.txt");
        Assert.DoesNotContain(
            result.SourceScan.AllFiles,
            file => file.Name == "b.txt" && file.FullPath.Contains("compressed", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(tree.DestExisting.Size, destOriginal.Size);
        Assert.Contains(tree.AnchorFoo.Files, file => file.Name == "a.txt");
        Assert.Contains(tree.SourceFoo.Files, file => file.Name == "a.txt");
        Assert.Empty(tree.Compressed.Folders.SelectMany(folder => folder.Files).Where(file => file.Name == "b.txt"));
    }

    [Fact]
    public void OverwriteReplacesDestAndRemovesSource()
    {
        var tree = OverlappingTree();
        var plan = IndexedFileMerge.TryBuildPlan(
            [tree.Scan],
            tree.Compressed,
            tree.AnchorSpan,
            CollisionPolicy.Overwrite).Plan!;
        var mover = new RecordingFileMover();
        mover.Existing.Add(tree.DestExisting.FullPath);

        var result = IndexedFileMerge.Execute(plan, mover);

        Assert.Equal(3, result.Moved);
        Assert.Equal(0, result.Skipped);
        Assert.DoesNotContain(result.SourceScan.AllFiles, file => Same(file.FullPath, tree.SourceCollision.FullPath));
        var destA = Assert.Single(result.DestScan.AllFiles, file => file.Name == "a.txt" && file.FullPath.Contains("Anchor", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(tree.SourceCollision.Size, destA.Size);
        Assert.Contains(mover.Moves, pair => pair.Overwrite && pair.Dest.EndsWith(@"Foo\a.txt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IndexSizesFollowTheMovedFile()
    {
        var tree = OverlappingTree();
        var plan = IndexedFileMerge.TryBuildPlan(
            [tree.Scan],
            tree.Compressed,
            tree.AnchorSpan,
            CollisionPolicy.SkipExisting).Plan!;
        var sourceBefore = tree.Compressed.Size;
        var destBefore = tree.AnchorSpan.Size;
        var movedSize = tree.SourceKeep.Size + tree.Zip.Size;

        var result = IndexedFileMerge.Execute(plan, new RecordingFileMover());

        Assert.Equal(sourceBefore - movedSize, result.SourceScan.Root.Folders.Single(folder => folder.Name == "compressed").Size);
        Assert.Equal(destBefore + movedSize, result.DestScan.Root.Folders.Single(folder => folder.Name == "Anchor Span").Size);
        Assert.Contains(tree.AnchorFoo.Files, file => file.Name == "b.txt");
        Assert.True(tree.AnchorSpan.Folders.Any(folder => folder.Name == "zip")
            || tree.AnchorSpan.Folders.SelectMany(folder => folder.Folders).Any());
        Assert.Contains(result.DestScan.AllFiles, file => file.Name == "anchor_span.zip");
    }

    [Fact]
    public void FilesNodeCannotBeASource()
    {
        var tree = OverlappingTree();
        FolderFilesNode.Attach(tree.Root);
        var files = tree.SourceFoo.TreeChildren.Single(node => node.IsFilesNode);
        var result = IndexedFileMerge.TryBuildPlan([tree.Scan], files, tree.AnchorSpan, CollisionPolicy.SkipExisting);
        Assert.Null(result.Plan);
        Assert.Contains("FILES", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void DestOutsideTheIndexAsksForAScan()
    {
        var tree = OverlappingTree();
        var result = IndexedFileMerge.TryBuildPlan(
            [tree.Scan],
            tree.Compressed,
            @"E:\not-scanned\Anchor Span",
            CollisionPolicy.SkipExisting);
        Assert.Null(result.Plan);
        Assert.True(result.DestNeedsScan);
        Assert.Contains("Scan it first", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsMergingIntoAChildOfTheSource()
    {
        var tree = OverlappingTree();
        var result = IndexedFileMerge.TryBuildPlan(
            [tree.Scan],
            tree.Compressed,
            tree.SourceFoo,
            CollisionPolicy.SkipExisting);
        Assert.Null(result.Plan);
        Assert.Contains("inside", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ErrorsCountAndContinueWithoutRollingBack()
    {
        var tree = OverlappingTree();
        var plan = IndexedFileMerge.TryBuildPlan(
            [tree.Scan],
            tree.Compressed,
            tree.AnchorSpan,
            CollisionPolicy.SkipExisting).Plan!;
        var mover = new RecordingFileMover
        {
            Failures = { [tree.Zip.FullPath] = new IOException("in use") },
        };

        var result = IndexedFileMerge.Execute(plan, mover);
        Assert.Equal(1, result.Moved);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(1, result.Failed);
        Assert.Contains(result.SourceScan.AllFiles, file => file.Name == "anchor_span.zip");
        Assert.DoesNotContain(
            result.SourceScan.AllFiles,
            file => file.Name == "b.txt" && file.FullPath.Contains("compressed", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Moved 1/3, skipped 1, failed 1", IndexedFileMerge.ProgressText(1, 3, 1, 1));
    }

    [Fact]
    public void CancelStopsStartingNewFiles()
    {
        var tree = OverlappingTree();
        var plan = IndexedFileMerge.TryBuildPlan(
            [tree.Scan],
            tree.Compressed,
            tree.AnchorSpan,
            CollisionPolicy.Overwrite).Plan!;
        using var cts = new CancellationTokenSource();
        var mover = new RecordingFileMover();
        mover.OnMove = (_, _, _) =>
        {
            if (mover.Moves.Count >= 1)
            {
                cts.Cancel();
            }
        };

        var result = IndexedFileMerge.Execute(plan, mover, cancellationToken: cts.Token);
        Assert.True(result.Cancelled);
        Assert.True(result.Moved >= 1);
        Assert.True(result.Moved < plan.FileCount);
        Assert.True(result.Moved + result.Failed + result.Skipped < plan.FileCount || result.Cancelled);
    }

    [Fact]
    public void MergeIsEditModeOnly()
    {
        Assert.False(UiInteractionMode.CanMergeFolder(isEditMode: false, isScanning: false, isRealFolder: true));
        Assert.False(UiInteractionMode.CanMergeFolder(isEditMode: true, isScanning: true, isRealFolder: true));
        Assert.False(UiInteractionMode.CanMergeFolder(isEditMode: true, isScanning: false, isRealFolder: false));
        Assert.True(UiInteractionMode.CanMergeFolder(isEditMode: true, isScanning: false, isRealFolder: true));
        Assert.Equal("Merge into…", UiInteractionMode.MergeIntoHeader);
        Assert.Contains("merge", UiInteractionMode.EditBanner, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UncSameShareIsRename()
    {
        var share = @"\\nas\record";
        var root = Folder("EngA", share + @"\EngA");
        var dest = Folder("Anchor Span", share + @"\EngA\Anchor Span", root);
        var source = Folder("compressed", share + @"\EngA\compressed", root);
        var file = AddFile(source, "a.zip", share + @"\EngA\compressed\a.zip", 10);
        source.Size = 10;
        source.FileCount = 1;
        root.Folders.Add(dest);
        root.Folders.Add(source);
        root.Size = 10;
        root.FileCount = 1;
        root.FolderCount = 2;
        FolderFilesNode.Attach(root);
        var scan = new ScanResult { Root = root, AllFiles = [file] };

        var plan = IndexedFileMerge.TryBuildPlan([scan], source, dest, CollisionPolicy.SkipExisting).Plan!;
        Assert.True(plan.SameVolume);
        var mover = new RecordingFileMover();
        IndexedFileMerge.Execute(plan, mover);
        Assert.Single(mover.Moves);
        Assert.Empty(mover.Copies);
        Assert.Equal(share + @"\EngA\Anchor Span\a.zip", mover.Moves[0].Dest, StringComparer.OrdinalIgnoreCase);
    }

    private static Overlap OverlappingTree()
    {
        var root = Folder("EngA data drive", @"C:\Record Copy\Incoming\EngA data drive");
        var dest = Folder("Anchor Span", @"C:\Record Copy\Incoming\EngA data drive\Anchor Span", root);
        var destFoo = Folder("Foo", @"C:\Record Copy\Incoming\EngA data drive\Anchor Span\Foo", dest);
        var source = Folder("compressed", @"C:\Record Copy\Incoming\EngA data drive\compressed", root);
        var sourceFoo = Folder("Foo", @"C:\Record Copy\Incoming\EngA data drive\compressed\Foo", source);
        var zipFolder = Folder("zip", @"C:\Record Copy\Incoming\EngA data drive\compressed\zip", source);

        var destA = AddFile(destFoo, "a.txt", destFoo.FullPath + @"\a.txt", 20);
        var sourceA = AddFile(sourceFoo, "a.txt", sourceFoo.FullPath + @"\a.txt", 50);
        var sourceB = AddFile(sourceFoo, "b.txt", sourceFoo.FullPath + @"\b.txt", 5);
        var zip = AddFile(zipFolder, "anchor_span.zip", zipFolder.FullPath + @"\anchor_span.zip", 100);

        destFoo.Size = destA.Size;
        destFoo.FileCount = 1;
        dest.Folders.Add(destFoo);
        dest.Size = destA.Size;
        dest.FileCount = 1;
        dest.FolderCount = 1;

        sourceFoo.Size = sourceA.Size + sourceB.Size;
        sourceFoo.FileCount = 2;
        zipFolder.Size = zip.Size;
        zipFolder.FileCount = 1;
        source.Folders.Add(sourceFoo);
        source.Folders.Add(zipFolder);
        source.Size = sourceFoo.Size + zipFolder.Size;
        source.FileCount = 3;
        source.FolderCount = 2;

        root.Folders.Add(dest);
        root.Folders.Add(source);
        root.Size = dest.Size + source.Size;
        root.FileCount = dest.FileCount + source.FileCount;
        root.FolderCount = 2 + dest.FolderCount + source.FolderCount;
        FolderFilesNode.Attach(root);

        var scan = new ScanResult
        {
            Root = root,
            AllFiles = [destA, sourceA, sourceB, zip],
        };
        return new Overlap(scan, root, dest, destFoo, source, sourceFoo, destA, sourceA, sourceB, zip);
    }

    private static FolderNode Folder(string name, string path, FolderNode? parent = null) =>
        new()
        {
            Name = name,
            FullPath = path,
            Parent = parent,
        };

    private static FileEntry AddFile(FolderNode folder, string name, string path, long size)
    {
        var file = new FileEntry
        {
            Name = name,
            FullPath = path,
            Size = size,
            Modified = DateTime.UnixEpoch,
            Parent = folder,
        };
        folder.Files.Add(file);
        return file;
    }

    private static bool Same(string left, string right) =>
        IndexedPath.TryCanonicalize(left, out var a)
        && IndexedPath.TryCanonicalize(right, out var b)
        && a.Equals(b, LocalPathGuard.Comparison);

    private sealed record Overlap(
        ScanResult Scan,
        FolderNode Root,
        FolderNode AnchorSpan,
        FolderNode AnchorFoo,
        FolderNode Compressed,
        FolderNode SourceFoo,
        FileEntry DestExisting,
        FileEntry SourceCollision,
        FileEntry SourceKeep,
        FileEntry Zip);

    private sealed class RecordingFileMover : IFileMover
    {
        public List<(string Source, string Dest, bool Overwrite)> Moves { get; } = [];
        public List<(string Source, string Dest, bool Overwrite)> Copies { get; } = [];
        public List<string> Directories { get; } = [];
        public HashSet<string> Existing { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Exception> Failures { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Action<string, string, bool>? OnMove { get; set; }

        public void EnsureDirectory(string directoryPath) => Directories.Add(directoryPath);

        public bool FileExists(string fullPath) => Existing.Contains(fullPath);

        public void Move(string sourcePath, string destPath, bool overwrite)
        {
            ThrowIfFailed(sourcePath);
            OnMove?.Invoke(sourcePath, destPath, overwrite);
            Moves.Add((sourcePath, destPath, overwrite));
            Existing.Remove(sourcePath);
            Existing.Add(destPath);
        }

        public void CopyThenDelete(string sourcePath, string destPath, bool overwrite)
        {
            ThrowIfFailed(sourcePath);
            Copies.Add((sourcePath, destPath, overwrite));
            Existing.Remove(sourcePath);
            Existing.Add(destPath);
        }

        private void ThrowIfFailed(string sourcePath)
        {
            if (Failures.TryGetValue(sourcePath, out var error))
            {
                throw error;
            }
        }
    }
}
