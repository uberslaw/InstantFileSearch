using InstantFileSearch;

namespace InstantFileSearch.Tests;

public class FolderFilesNodeTests
{
    [Fact]
    public void AttachAddsFilesNodeOnlyWhenDirectFilesExist()
    {
        var root = Folder("root", "/tmp/root", size: 1150);
        var nested = Folder("sub", "/tmp/root/sub", size: 1000, parent: root);
        var onlyDirs = Folder("empty", "/tmp/root/empty", size: 1000, parent: root);
        var leaf = Folder("leaf", "/tmp/root/empty/leaf", size: 1000, parent: onlyDirs);

        AddFile(root, "a.bin", "/tmp/root/a.bin", 100);
        AddFile(root, "b.bin", "/tmp/root/b.bin", 50);
        AddFile(nested, "c.bin", "/tmp/root/sub/c.bin", 1000);
        AddFile(leaf, "deep.bin", "/tmp/root/empty/leaf/deep.bin", 1000);

        root.Folders.Add(nested);
        root.Folders.Add(onlyDirs);
        onlyDirs.Folders.Add(leaf);

        FolderFilesNode.Attach(root);

        Assert.DoesNotContain(root.Folders, node => node.IsFilesNode);
        Assert.DoesNotContain(nested.Folders, node => node.IsFilesNode);
        Assert.DoesNotContain(onlyDirs.Folders, node => node.IsFilesNode);

        var rootFiles = Assert.Single(root.TreeChildren, node => node.IsFilesNode);
        Assert.Equal(FolderFilesNode.DisplayName, rootFiles.Name);
        Assert.Equal(150, rootFiles.Size);
        Assert.Equal(2, rootFiles.FileCount);
        Assert.Equal(root.FullPath, rootFiles.FullPath);
        Assert.Same(root, rootFiles.Parent);
        Assert.Equal(ResultsUi.FilesGlyph(), rootFiles.Glyph);
        Assert.NotEqual(ResultsUi.FolderGlyph(isRoot: false, ScanLocationKind.Local), rootFiles.Glyph);

        Assert.Single(nested.TreeChildren, node => node.IsFilesNode);
        Assert.Equal(1000, nested.TreeChildren.Single(node => node.IsFilesNode).Size);

        Assert.DoesNotContain(onlyDirs.TreeChildren, node => node.IsFilesNode);
        Assert.Equal(leaf, Assert.Single(onlyDirs.TreeChildren));
        Assert.False(FolderFilesNode.IsScanRoot(rootFiles));
        Assert.True(FolderFilesNode.IsScanRoot(root));
        Assert.False(FolderFilesNode.IsScanRoot(nested));
        Assert.False(FolderFilesNode.IsScanRoot(onlyDirs));
        Assert.False(FolderFilesNode.IsScanRoot(null));
    }

    [Fact]
    public void FilesNodeSizeIgnoresNestedFilesAndSortsWithSiblings()
    {
        var root = Folder("root", "/tmp/root", size: 5000);
        var tiny = Folder("tiny", "/tmp/root/tiny", size: 10, parent: root);
        AddFile(root, "huge.bin", "/tmp/root/huge.bin", 4000);
        AddFile(tiny, "x.bin", "/tmp/root/tiny/x.bin", 10);
        root.Folders.Add(tiny);

        FolderFilesNode.Attach(root);

        Assert.Equal(["FILES", "tiny"], root.TreeChildren.Select(node => node.Name).ToList());
        Assert.Equal(4000, root.TreeChildren[0].Size);
        Assert.Equal(80, root.TreeChildren[0].PercentOfParent);
        Assert.DoesNotContain(root.TreeChildren, node => node.Name == "FILES" && node.Size == 4010);
    }

    [Fact]
    public void SelectingFilesListsOnlyDirectFiles()
    {
        var root = Folder("work", "/tmp/work", size: 300);
        var child = Folder("sub", "/tmp/work/sub", size: 200, parent: root);
        var direct = AddFile(root, "here.txt", "/tmp/work/here.txt", 100);
        AddFile(child, "nested.txt", "/tmp/work/sub/nested.txt", 200);
        root.Folders.Add(child);
        FolderFilesNode.Attach(root);

        var filesNode = root.TreeChildren.Single(node => node.IsFilesNode);
        Assert.Empty(FolderFilesNode.ContentFolders(filesNode));
        Assert.Equal([direct], FolderFilesNode.ContentFiles(filesNode));
        Assert.Equal("work", FolderFilesNode.OwnerName(filesNode));

        Assert.Equal([child], FolderFilesNode.ContentFolders(root));
        Assert.Equal([direct], FolderFilesNode.ContentFiles(root));
    }

    [Fact]
    public void ExcludeAndCopyTreatFilesAsTheParentFolderPath()
    {
        var root = Folder("work", @"C:\work", size: 10);
        AddFile(root, "a.txt", @"C:\work\a.txt", 10);
        FolderFilesNode.Attach(root);
        var filesNode = root.TreeChildren.Single(node => node.IsFilesNode);

        Assert.False(FolderFilesNode.CanExclude(filesNode));
        Assert.False(FolderFilesNode.CanExclude(root));
        Assert.Equal(@"C:\work", filesNode.FullPath);
        Assert.Equal(@"C:\work", ResultsUi.TreeExplorerPath(filesNode));
        Assert.Equal(@"C:\work", ResultsUi.TreeExplorerPath(root));
        Assert.Equal(@"C:\work", ResultsUi.ContextPath(@"C:\work\a.txt", filesNode.FullPath, treeContext: true));
        Assert.Equal(FolderFilesNode.DisplayName, ResultsUi.ContextName("a.txt", filesNode.Name, treeContext: true));
        Assert.Equal("Files  ·  10 B", ResultsUi.DetailsMeta(filesNode.Size, DateTime.MinValue, isFolder: true, isFilesNode: true));
    }

    [Fact]
    public void SearchScopeOnFilesDoesNotIncludeNestedFiles()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "ifs-files-" + Guid.NewGuid().ToString("N"));
        var root = Folder("root", rootPath, size: 30);
        var child = Folder("sub", Path.Combine(rootPath, "sub"), size: 20, parent: root);
        var direct = AddFile(root, "top.txt", Path.Combine(rootPath, "top.txt"), 10);
        var nested = AddFile(child, "deep.txt", Path.Combine(rootPath, "sub", "deep.txt"), 20);
        root.Folders.Add(child);
        FolderFilesNode.Attach(root);
        var filesNode = root.TreeChildren.Single(node => node.IsFilesNode);
        var all = new[] { direct, nested };

        var scoped = FolderFilesNode.FilesForSearch(all, filesNode, selectedFolderScope: true).ToList();
        Assert.Equal([direct], scoped);
        Assert.Equal(all, FolderFilesNode.FilesForSearch(all, filesNode, selectedFolderScope: false));

        Assert.Empty(FolderFilesNode.FoldersForSearch([root], filesNode, selectedFolderScope: true));
        var descendants = FolderFilesNode.FoldersForSearch([root], root, selectedFolderScope: true).ToList();
        Assert.Equal(["sub"], descendants.Select(node => node.Name).ToList());
        Assert.DoesNotContain(descendants, node => node.IsFilesNode);
        Assert.Contains(FolderFilesNode.FoldersForSearch([root], selected: null, selectedFolderScope: false), node => node.Name == "root");

        var matches = FileNameSearch.Filter(all, new SearchQuery
        {
            UnderFolder = filesNode.FullPath,
            DirectChildrenOnly = true,
        }).Select(file => file.Name).ToList();
        Assert.Equal(["top.txt"], matches);

        var underFolder = FileNameSearch.Filter(all, new SearchQuery
        {
            UnderFolder = root.FullPath,
        }).Select(file => file.Name).OrderBy(name => name).ToList();
        Assert.Equal(["deep.txt", "top.txt"], underFolder);
    }

    [Fact]
    public void ScanBuildsFilesNodesWithoutIndexingThem()
    {
        var root = Path.Combine(Path.GetTempPath(), "ifs-files-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "only-sub", "leaf"));
        Directory.CreateDirectory(Path.Combine(root, "mixed"));
        File.WriteAllBytes(Path.Combine(root, "only-sub", "leaf", "deep.bin"), new byte[64]);
        File.WriteAllBytes(Path.Combine(root, "mixed", "here.bin"), new byte[32]);
        File.WriteAllBytes(Path.Combine(root, "root.log"), new byte[16]);

        try
        {
            var result = new FileScanner().Scan(root);
            Assert.Equal(3, result.AllFiles.Count);
            Assert.DoesNotContain(result.AllFiles, file => file.Name == FolderFilesNode.DisplayName);
            Assert.DoesNotContain(result.Root.Folders, folder => folder.IsFilesNode);

            var rootFiles = Assert.Single(result.Root.TreeChildren, node => node.IsFilesNode);
            Assert.Equal(16, rootFiles.Size);
            Assert.Equal(["root.log"], FolderFilesNode.ContentFiles(rootFiles).Select(file => file.Name));
            Assert.Empty(FolderFilesNode.ContentFolders(rootFiles));

            var mixed = result.Root.Folders.Single(folder => folder.Name == "mixed");
            Assert.Equal(32, mixed.TreeChildren.Single(node => node.IsFilesNode).Size);

            var onlySub = result.Root.Folders.Single(folder => folder.Name == "only-sub");
            Assert.DoesNotContain(onlySub.TreeChildren, node => node.IsFilesNode);
            Assert.False(FolderFilesNode.CanExclude(rootFiles));
            Assert.True(FolderFilesNode.CanExclude(mixed));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CacheReloadRebuildsFilesNodesAndDoesNotPersistThemAsFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "ifs-files-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "readme.txt"), new byte[128]);
        var cache = Path.Combine(Path.GetTempPath(), "ifs-files-last-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            var original = new FileScanner().Scan(root);
            ScanCache.Save(original, cache, new PassThroughByteProtector());
            Assert.True(ProtectedFile.TryReadAll(cache, new PassThroughByteProtector(), out var bytes));
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            Assert.DoesNotContain("\"Name\":\"FILES\"", json, StringComparison.Ordinal);

            Assert.True(ScanCache.TryLoad(cache, out var loaded, new PassThroughByteProtector()));
            Assert.NotNull(loaded);
            Assert.DoesNotContain(loaded!.Root.Folders, folder => folder.IsFilesNode);
            var filesNode = Assert.Single(loaded.Root.TreeChildren, node => node.IsFilesNode);
            Assert.Equal(128, filesNode.Size);
            Assert.Equal(loaded.Root.FullPath, filesNode.FullPath);
            Assert.Equal(original.AllFiles.Count, loaded.AllFiles.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            if (File.Exists(cache))
            {
                File.Delete(cache);
            }
        }
    }

    private static FolderNode Folder(string name, string path, long size, FolderNode? parent = null) =>
        new()
        {
            Name = name,
            FullPath = path,
            Parent = parent,
            Size = size,
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
}
