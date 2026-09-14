namespace InstantFileSearch;

public sealed class FolderNode
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public FolderNode? Parent { get; set; }
    public List<FolderNode> Folders { get; } = [];
    public List<FileEntry> Files { get; } = [];
    public long Size { get; set; }
    public int FileCount { get; set; }
    public int FolderCount { get; set; }
    public DateTime Modified { get; set; }

    public double PercentOfParent =>
        Parent is null || Parent.Size <= 0 ? 100 : Size * 100.0 / Parent.Size;
}
