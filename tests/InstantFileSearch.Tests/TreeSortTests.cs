using InstantFileSearch;

namespace InstantFileSearch.Tests;

public class TreeSortTests
{
    [Fact]
    public void LabelsAndParseRoundTripIncludingDefault()
    {
        Assert.Equal(TreeSortMode.SizeDescending, TreeSort.Default);
        Assert.Equal(TreeSort.SizeDescLabel, TreeSort.Label(TreeSort.Default));
        Assert.Equal(["A–Z", "Z–A", "Size ↓", "Size ↑"], TreeSort.Labels.ToList());
        Assert.Equal(TreeSortMode.NameAscending, TreeSort.Parse("A–Z"));
        Assert.Equal(TreeSortMode.NameAscending, TreeSort.Parse("A-Z"));
        Assert.Equal(TreeSortMode.NameDescending, TreeSort.Parse("Z–A"));
        Assert.Equal(TreeSortMode.SizeAscending, TreeSort.Parse("Size ↑"));
        Assert.Equal(TreeSortMode.SizeDescending, TreeSort.Parse("Max–Min"));
        Assert.Equal(TreeSort.Default, TreeSort.Parse("nope"));
        Assert.Equal(TreeSort.Default, TreeSort.Parse(""));
        Assert.Equal(TreeSort.Default, TreeSort.Parse(null));
        foreach (var mode in Enum.GetValues<TreeSortMode>())
        {
            Assert.Equal(mode, TreeSort.Parse(TreeSort.Label(mode)));
            Assert.Equal(mode, TreeSort.Parse(mode.ToString()));
        }
    }

    [Fact]
    public void FilesNodeSortsWithSiblingsByNameAndSize()
    {
        var root = Node("root", "/tmp/root", 5000);
        var zebra = Node("zebra", "/tmp/root/zebra", 10, root);
        var alpha = Node("alpha", "/tmp/root/alpha", 2000, root);
        AddFile(root, "huge.bin", 4000);
        root.Folders.Add(zebra);
        root.Folders.Add(alpha);
        FolderFilesNode.Attach(root);

        Assert.Equal(["FILES", "alpha", "zebra"], Names(root.TreeChildren));

        TreeSort.Apply(root, TreeSortMode.NameAscending);
        Assert.Equal(["alpha", "FILES", "zebra"], Names(root.TreeChildren));

        TreeSort.Apply(root, TreeSortMode.NameDescending);
        Assert.Equal(["zebra", "FILES", "alpha"], Names(root.TreeChildren));

        TreeSort.Apply(root, TreeSortMode.SizeDescending);
        Assert.Equal(["FILES", "alpha", "zebra"], Names(root.TreeChildren));
        Assert.Equal(4000, root.TreeChildren[0].Size);

        TreeSort.Apply(root, TreeSortMode.SizeAscending);
        Assert.Equal(["zebra", "alpha", "FILES"], Names(root.TreeChildren));
    }

    [Fact]
    public void OrderRootsSortsScanRootsByTheSameKey()
    {
        var small = Node("b-small", "/tmp/b", 10);
        var large = Node("a-large", "/tmp/a", 99);
        Assert.Equal(["a-large", "b-small"], Names(TreeSort.OrderRoots([small, large], TreeSortMode.NameAscending)));
        Assert.Equal(["b-small", "a-large"], Names(TreeSort.OrderRoots([small, large], TreeSortMode.NameDescending)));
        Assert.Equal(["a-large", "b-small"], Names(TreeSort.OrderRoots([small, large], TreeSortMode.SizeDescending)));
        Assert.Equal(["b-small", "a-large"], Names(TreeSort.OrderRoots([small, large], TreeSortMode.SizeAscending)));
    }

    private static FolderNode Node(string name, string path, long size, FolderNode? parent = null) =>
        new()
        {
            Name = name,
            FullPath = path,
            Parent = parent,
            Size = size,
        };

    private static void AddFile(FolderNode folder, string name, long size)
    {
        folder.Files.Add(new FileEntry
        {
            Name = name,
            FullPath = folder.FullPath + "/" + name,
            Size = size,
            Modified = DateTime.UnixEpoch,
            Parent = folder,
        });
        folder.Size = Math.Max(folder.Size, size);
    }

    private static List<string> Names(IEnumerable<FolderNode> nodes) =>
        nodes.Select(node => node.Name).ToList();
}
