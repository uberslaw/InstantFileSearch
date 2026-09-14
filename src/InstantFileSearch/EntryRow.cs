namespace InstantFileSearch;

public sealed class EntryRow
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required string Kind { get; init; }
    public bool IsFolder { get; init; }
    public long Size { get; init; }
    public string SizeText => ByteFormatter.ToString(Size);
    public DateTime Modified { get; init; }
    public string ModifiedText => Modified == DateTime.MinValue ? "" : Modified.ToString("yyyy-MM-dd HH:mm");
    public double Percent { get; init; }
    public FolderNode? Folder { get; init; }
    public FileEntry? File { get; init; }
    public string Glyph => IsFolder ? "\uE8B7" : "\uE7C3";
}
