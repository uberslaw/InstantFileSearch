namespace InstantFileSearch;

/// <summary>
/// One search row: a real file or a real folder. FILES synthetic nodes are never hits.
/// </summary>
public sealed class SearchHit
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public long Size { get; init; }
    public DateTime Modified { get; init; }
    public bool IsFolder { get; init; }
    public FolderNode? Folder { get; init; }
    public FileEntry? File { get; init; }

    public static SearchHit FromFolder(FolderNode folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return new SearchHit
        {
            Name = folder.Name,
            FullPath = folder.FullPath,
            Size = folder.Size,
            Modified = folder.Modified,
            IsFolder = true,
            Folder = folder,
        };
    }

    public static SearchHit FromFile(FileEntry file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new SearchHit
        {
            Name = file.Name,
            FullPath = file.FullPath,
            Size = file.Size,
            Modified = file.Modified,
            IsFolder = false,
            File = file,
        };
    }
}
