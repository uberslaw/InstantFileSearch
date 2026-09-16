using InstantFileSearch;

namespace InstantFileSearch.Tests;

public class SearchFilterTests
{
    private static FileEntry File(string name, string path, long size, DateTime? modified = null) =>
        new()
        {
            Name = name,
            FullPath = path,
            Size = size,
            Modified = modified ?? DateTime.UnixEpoch,
        };

    [Fact]
    public void FromOneMegabyteExcludesSmallerFiles()
    {
        var files = new[]
        {
            File("tiny.bin", "/tmp/tiny.bin", 1024),
            File("just.bin", "/tmp/just.bin", ByteFormatter.ToBytes(1, ByteSizeUnit.MB)),
            File("huge.bin", "/tmp/huge.bin", ByteFormatter.ToBytes(2, ByteSizeUnit.MB)),
        };

        var matches = FileNameSearch.Filter(files, new SearchQuery
        {
            MinSizeBytes = ByteFormatter.ToBytes(1, ByteSizeUnit.MB),
        }).Select(file => file.Name).ToList();

        Assert.Equal(["just.bin", "huge.bin"], matches);
    }

    [Fact]
    public void ToOneKilobyteExcludesLargerFiles()
    {
        var files = new[]
        {
            File("small.bin", "/tmp/small.bin", 512),
            File("edge.bin", "/tmp/edge.bin", 1024),
            File("big.bin", "/tmp/big.bin", 2048),
        };

        var matches = FileNameSearch.Filter(files, new SearchQuery
        {
            MaxSizeBytes = ByteFormatter.ToBytes(1, ByteSizeUnit.KB),
        }).Select(file => file.Name).ToList();

        Assert.Equal(["small.bin", "edge.bin"], matches);
    }

    [Fact]
    public void EmptySizeBoundsDoNotFilter()
    {
        var files = new[]
        {
            File("a.bin", "/tmp/a.bin", 1),
            File("b.bin", "/tmp/b.bin", 8 * 1024 * 1024),
        };

        var matches = FileNameSearch.Filter(files, new SearchQuery { Text = "*.bin" }).ToList();
        Assert.Equal(2, matches.Count);
        Assert.Empty(FileNameSearch.Filter(files, new SearchQuery()));
        Assert.Null(ByteFormatter.ParseOptionalBytes("", ByteSizeUnit.MB));
        Assert.Null(ByteFormatter.ParseOptionalBytes("  ", ByteSizeUnit.KB));
        Assert.Null(ByteFormatter.ParseOptionalBytes(null, ByteSizeUnit.GB));
    }

    [Fact]
    public void SizeOnlyQueryIsAllowedWithEmptyText()
    {
        var files = new[]
        {
            File("keep.bin", "/tmp/keep.bin", ByteFormatter.ToBytes(3, ByteSizeUnit.MB)),
            File("skip.bin", "/tmp/skip.bin", 100),
        };

        var matches = FileNameSearch.Filter(files, new SearchQuery
        {
            MinSizeBytes = ByteFormatter.ToBytes(1, ByteSizeUnit.MB),
        }).ToList();

        Assert.Single(matches);
        Assert.Equal("keep.bin", matches[0].Name);
    }

    [Fact]
    public void UnitsAre1024Based()
    {
        Assert.Equal(1, ByteFormatter.ToBytes(1, ByteSizeUnit.B));
        Assert.Equal(1024, ByteFormatter.ToBytes(1, ByteSizeUnit.KB));
        Assert.Equal(1024 * 1024, ByteFormatter.ToBytes(1, ByteSizeUnit.MB));
        Assert.Equal(1024L * 1024 * 1024, ByteFormatter.ToBytes(1, ByteSizeUnit.GB));
        Assert.Equal(1024L * 1024 * 1024 * 1024, ByteFormatter.ToBytes(1, ByteSizeUnit.TB));
        Assert.Equal(1024, ByteFormatter.ParseOptionalBytes("1", ByteSizeUnit.KB));
        Assert.Equal(1536, ByteFormatter.ParseOptionalBytes("1.5", ByteSizeUnit.KB));
        Assert.Equal(ByteSizeUnit.KB, ByteFormatter.ParseUnit("kb"));
        Assert.Equal(ByteSizeUnit.MB, ByteFormatter.ParseUnit("nope"));
    }

    [Fact]
    public void ModifiedFromAndToAreInclusiveDates()
    {
        var files = new[]
        {
            File("old.txt", "/tmp/old.txt", 1, new DateTime(2024, 6, 14, 23, 59, 0)),
            File("mid.txt", "/tmp/mid.txt", 1, new DateTime(2024, 6, 15, 23, 59, 0)),
            File("new.txt", "/tmp/new.txt", 1, new DateTime(2024, 6, 16, 0, 1, 0)),
        };

        var from = SearchQuery.ParseDate("2024-06-15");
        var to = SearchQuery.ParseDate("2024-06-15");
        Assert.Equal(new DateTime(2024, 6, 15), from);
        Assert.Null(SearchQuery.ParseDate(""));
        Assert.Null(SearchQuery.ParseDate("15/06/2024"));

        var matches = FileNameSearch.Filter(files, new SearchQuery
        {
            ModifiedFrom = from,
            ModifiedTo = to,
        }).Select(file => file.Name).ToList();

        Assert.Equal(["mid.txt"], matches);
        Assert.Empty(FileNameSearch.Filter(files, new SearchQuery
        {
            ModifiedFrom = SearchQuery.ParseDate("2024-06-16"),
        }).Where(file => file.Name == "old.txt"));
    }

    [Fact]
    public void SelectedFolderUsesIsSameOrUnder()
    {
        var root = Path.Combine(Path.GetTempPath(), "ifs-scope-" + Guid.NewGuid().ToString("N"));
        var other = root + "-sibling";
        var files = new[]
        {
            File("inside.txt", Path.Combine(root, "inside.txt"), 1),
            File("nested.txt", Path.Combine(root, "sub", "nested.txt"), 1),
            File("outside.txt", Path.Combine(other, "outside.txt"), 1),
        };

        var matches = FileNameSearch.Filter(files, new SearchQuery { UnderFolder = root })
            .Select(file => file.Name)
            .ToList();

        Assert.Equal(["inside.txt", "nested.txt"], matches);
        Assert.Empty(FileNameSearch.Filter(files, new SearchQuery { UnderFolder = "" }));
        Assert.Equal(3, FileNameSearch.Filter(files, new SearchQuery { Text = "*.txt" }).Count());
    }

    [Fact]
    public void MatchNameIsWholeNameNotPath()
    {
        var files = new[]
        {
            File("notes.txt", "/tmp/unique-token/notes.txt", 1),
            File("unique-token.log", "/tmp/other/unique-token.log", 1),
        };

        Assert.False(FileNameSearch.Matches(files[0], "unique-token", false, SearchMatchMode.Name));
        Assert.False(FileNameSearch.Matches(files[1], "unique-token", false, SearchMatchMode.Name));
        Assert.True(FileNameSearch.Matches(files[1], "unique-token.log", false, SearchMatchMode.Name));
        Assert.True(FileNameSearch.Matches(files[1], "unique-token*", true, SearchMatchMode.Name));

        Assert.Empty(FileNameSearch.Filter(files, new SearchQuery
        {
            Text = "unique-token",
            Match = SearchMatchMode.Name,
        }));
        Assert.Equal(["unique-token.log"], FileNameSearch.Filter(files, new SearchQuery
        {
            Text = "unique-token.log",
            Match = SearchMatchMode.Name,
        }).Select(file => file.Name).ToList());

        Assert.Equal(["notes.txt"], FileNameSearch.Filter(files, new SearchQuery
        {
            Text = files[0].FullPath,
            Match = SearchMatchMode.Path,
        }).Select(file => file.Name).ToList());
        Assert.Empty(FileNameSearch.Filter(files, new SearchQuery
        {
            Text = "unique-token",
            Match = SearchMatchMode.Path,
        }));
        Assert.Equal(2, FileNameSearch.Filter(files, new SearchQuery
        {
            Text = "*unique-token*",
            Match = SearchMatchMode.Path,
        }).Count());

        Assert.Equal(["unique-token.log"], FileNameSearch.Filter(files, new SearchQuery
        {
            Text = "unique-token.log",
            Match = SearchMatchMode.NameOrPath,
        }).Select(file => file.Name).ToList());
    }

    [Fact]
    public void TextAndSizeApplyTogether()
    {
        var files = new[]
        {
            File("report.txt", "/tmp/report.txt", ByteFormatter.ToBytes(2, ByteSizeUnit.MB)),
            File("report.txt", "/tmp/small/report.txt", 10),
            File("other.txt", "/tmp/other.txt", ByteFormatter.ToBytes(2, ByteSizeUnit.MB)),
        };

        var matches = FileNameSearch.Filter(files, new SearchQuery
        {
            Text = "report.txt",
            MinSizeBytes = ByteFormatter.ToBytes(1, ByteSizeUnit.MB),
        }).ToList();

        Assert.Single(matches);
        Assert.Equal("/tmp/report.txt", matches[0].FullPath);
    }

    [Theory]
    [InlineData("cmd", "cmd", true)]
    [InlineData("CMD", "cmd", true)]
    [InlineData("cmd", "cmd.exe", false)]
    [InlineData("cmd", "anythingwithcmdinit", false)]
    [InlineData("*cmd", "cmd", true)]
    [InlineData("*cmd", "foocmd", true)]
    [InlineData("*cmd", "cmd.exe", false)]
    [InlineData("*cmd*", "anythingwithcmdinit", true)]
    [InlineData("*cmd*", "cmd.exe", true)]
    [InlineData("cmd*", "cmd", true)]
    [InlineData("cmd*", "cmd.exe", true)]
    [InlineData("c?d", "cmd", true)]
    [InlineData("c?d", "cd", false)]
    public void NameMatchRequiresWildcardForPartial(string pattern, string name, bool expected)
    {
        var wildcard = FileNameSearch.HasWildcard(pattern);
        Assert.Equal(expected, FileNameSearch.Matches(name, "/tmp/" + name, pattern, wildcard, SearchMatchMode.Name));
    }

    [Fact]
    public void SearchIncludesFoldersAndSkipsFilesNode()
    {
        var root = new FolderNode { Name = "work", FullPath = "/tmp/work", Size = 50 };
        var cmd = new FolderNode { Name = "cmd", FullPath = "/tmp/work/cmd", Parent = root, Size = 10 };
        var nested = new FolderNode { Name = "anythingwithcmdinit", FullPath = "/tmp/work/anythingwithcmdinit", Parent = root, Size = 5 };
        root.Folders.Add(cmd);
        root.Folders.Add(nested);
        AddFile(root, "cmd.exe", "/tmp/work/cmd.exe", 20);
        AddFile(cmd, "inside.bin", "/tmp/work/cmd/inside.bin", 10);
        FolderFilesNode.Attach(root);
        var filesNode = root.TreeChildren.Single(node => node.IsFilesNode);

        var folders = FolderFilesNode.FoldersForSearch([root], selected: null, selectedFolderScope: false).ToList();
        Assert.Contains(folders, node => node.Name == "cmd");
        Assert.DoesNotContain(folders, node => node.IsFilesNode);
        Assert.DoesNotContain(folders, node => ReferenceEquals(node, filesNode));

        var files = new[] { root.Files[0], cmd.Files[0] };
        var hits = FileNameSearch.FilterHits(folders, files, new SearchQuery { Text = "cmd" }).ToList();
        Assert.Equal(["cmd"], hits.Select(hit => hit.Name).ToList());
        Assert.True(hits[0].IsFolder);

        Assert.Equal(["cmd.exe"], FileNameSearch.FilterHits(folders, files, new SearchQuery { Text = "cmd.exe" }).Select(hit => hit.Name).ToList());
        Assert.Equal(
            ["cmd", "cmd.exe"],
            FileNameSearch.FilterHits(folders, files, new SearchQuery { Text = "cmd*" })
                .Select(hit => hit.Name)
                .OrderBy(name => name)
                .ToList());

        var contains = FileNameSearch.FilterHits(folders, files, new SearchQuery
            {
                Text = "*cmd*",
                Match = SearchMatchMode.Name,
            })
            .Select(hit => hit.Name)
            .OrderBy(name => name)
            .ToList();
        Assert.Equal(["anythingwithcmdinit", "cmd", "cmd.exe"], contains);

        var filesHits = FileNameSearch.FilterHits([filesNode], files, new SearchQuery { Text = "FILES" });
        Assert.Empty(filesHits);
    }

    [Fact]
    public void PathWithoutWildcardEqualsFullPath()
    {
        var file = File("cmd", @"C:\Windows\System32\cmd", 1);
        Assert.False(FileNameSearch.Matches(file, "cmd", false, SearchMatchMode.Path));
        Assert.True(FileNameSearch.Matches(file, file.FullPath, false, SearchMatchMode.Path));
        Assert.True(FileNameSearch.Matches(file, @"*\cmd", true, SearchMatchMode.Path));
        Assert.True(FileNameSearch.Matches(file, "cmd", false, SearchMatchMode.NameOrPath));
    }

    private static FileEntry AddFile(FolderNode folder, string name, string path, long size)
    {
        var file = File(name, path, size);
        folder.Files.Add(new FileEntry
        {
            Name = name,
            FullPath = path,
            Size = size,
            Modified = DateTime.UnixEpoch,
            Parent = folder,
        });
        return folder.Files[^1];
    }
}
