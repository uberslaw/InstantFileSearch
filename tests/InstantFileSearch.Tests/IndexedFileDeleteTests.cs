using InstantFileSearch;

namespace InstantFileSearch.Tests;

public class UiInteractionModeTests
{
    [Fact]
    public void DeleteIsOnlyEnabledInEditForAFileWhileIdle()
    {
        Assert.False(UiInteractionMode.CanDeleteFile(isEditMode: false, isScanning: false, isFile: true));
        Assert.False(UiInteractionMode.CanDeleteFile(isEditMode: true, isScanning: false, isFile: false));
        Assert.False(UiInteractionMode.CanDeleteFile(isEditMode: true, isScanning: true, isFile: true));
        Assert.True(UiInteractionMode.CanDeleteFile(isEditMode: true, isScanning: false, isFile: true));
        Assert.Equal("Delete…", UiInteractionMode.DeleteMenuHeader);
        Assert.Contains("right-click a file", UiInteractionMode.EditBanner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not Exclude", UiInteractionMode.EditBanner, StringComparison.OrdinalIgnoreCase);
    }
}

public class IndexedFileDeleteTests
{
    [Fact]
    public void RecycleBinIsForLocalWindowsPathsOnly()
    {
        Assert.False(IndexedFileDelete.UsesRecycleBin(@"\\server\share\a.txt"));
        if (OperatingSystem.IsWindows())
        {
            Assert.True(IndexedFileDelete.UsesRecycleBin(@"C:\work\a.txt"));
        }
        else
        {
            Assert.False(IndexedFileDelete.UsesRecycleBin("/tmp/a.txt"));
        }
    }

    [Fact]
    public void ConfirmMessageNamesTheFileAndIsNotExclude()
    {
        var recycle = IndexedFileDelete.ConfirmMessage("a.txt", @"C:\work\a.txt", recycleBin: true);
        Assert.Contains("a.txt", recycle, StringComparison.Ordinal);
        Assert.Contains(@"C:\work\a.txt", recycle, StringComparison.Ordinal);
        Assert.Contains("Recycle Bin", recycle, StringComparison.Ordinal);
        Assert.Contains("not Exclude", recycle, StringComparison.Ordinal);

        var permanent = IndexedFileDelete.ConfirmMessage("a.txt", @"\\server\share\a.txt", recycleBin: false);
        Assert.Contains("Permanently delete", permanent, StringComparison.Ordinal);
        Assert.Contains(@"\\server\share\a.txt", permanent, StringComparison.Ordinal);
    }

    [Fact]
    public void DeleterMockRemovesFileAndUpdatesParentSizes()
    {
        var root = new FolderNode { Name = "work", FullPath = "/tmp/ifs-del-root" };
        var nested = new FolderNode { Name = "sub", FullPath = "/tmp/ifs-del-root/sub", Parent = root };
        var keep = new FileEntry { Name = "keep.bin", FullPath = "/tmp/ifs-del-root/keep.bin", Size = 50, Parent = root };
        var gone = new FileEntry { Name = "gone.txt", FullPath = "/tmp/ifs-del-root/sub/gone.txt", Size = 100, Parent = nested };
        nested.Files.Add(gone);
        nested.Size = 100;
        nested.FileCount = 1;
        root.Folders.Add(nested);
        root.Files.Add(keep);
        root.Size = 150;
        root.FileCount = 2;
        root.FolderCount = 1;
        FolderFilesNode.Attach(root);

        var scan = new ScanResult
        {
            Root = root,
            AllFiles = [keep, gone],
        };
        var deleter = new RecordingFileDeleter();

        Assert.True(IndexedFileDelete.TryDeleteIndexedFile(scan, gone.FullPath, deleter, out var updated));
        Assert.True(LocalPathGuard.TryGetFullPath(gone.FullPath, out var deletedPath));
        Assert.Equal([deletedPath], deleter.Paths);
        Assert.DoesNotContain(updated.AllFiles, file => file.Name == "gone.txt");
        Assert.Contains(updated.AllFiles, file => file.Name == "keep.bin");
        Assert.Equal(50, updated.Root.Size);
        Assert.Equal(1, updated.Root.FileCount);
        Assert.Empty(nested.Files);
        Assert.Equal(0, nested.Size);
        Assert.Equal(0, nested.FileCount);
        Assert.DoesNotContain(nested.TreeChildren, node => node.IsFilesNode);
        Assert.Contains(root.TreeChildren, node => node.IsFilesNode);
    }

    [Fact]
    public void FailedDeleterLeavesTheIndexAlone()
    {
        var root = new FolderNode { Name = "work", FullPath = "/tmp/ifs-del-fail" };
        var file = new FileEntry { Name = "locked.txt", FullPath = "/tmp/ifs-del-fail/locked.txt", Size = 8, Parent = root };
        root.Files.Add(file);
        root.Size = 8;
        root.FileCount = 1;
        FolderFilesNode.Attach(root);
        var scan = new ScanResult { Root = root, AllFiles = [file] };
        var deleter = new RecordingFileDeleter { Throw = new IOException("in use") };

        var error = Assert.Throws<IOException>(() =>
            IndexedFileDelete.TryDeleteIndexedFile(scan, file.FullPath, deleter, out _));
        Assert.Equal("in use", error.Message);
        Assert.Empty(deleter.Paths);
        Assert.Single(scan.AllFiles);
        Assert.Equal(8, scan.Root.Size);
        Assert.Single(root.Files);
    }

    [Fact]
    public void UnknownPathIsNotDeleted()
    {
        var root = new FolderNode { Name = "work", FullPath = "/tmp/ifs-del-none" };
        var scan = new ScanResult { Root = root, AllFiles = [] };
        var deleter = new RecordingFileDeleter();
        Assert.False(IndexedFileDelete.TryDeleteIndexedFile(scan, "/tmp/ifs-del-none/missing.txt", deleter, out _));
        Assert.Empty(deleter.Paths);
    }

    private sealed class RecordingFileDeleter : IFileDeleter
    {
        public List<string> Paths { get; } = [];
        public Exception? Throw { get; set; }

        public void Delete(string fullPath)
        {
            if (Throw is not null)
            {
                throw Throw;
            }

            Paths.Add(fullPath);
        }
    }
}
