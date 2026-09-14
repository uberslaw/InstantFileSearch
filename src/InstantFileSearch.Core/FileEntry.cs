namespace InstantFileSearch;

public sealed class FileEntry
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public long Size { get; init; }
    public DateTime Modified { get; init; }
    public FolderNode? Parent { get; init; }
}
