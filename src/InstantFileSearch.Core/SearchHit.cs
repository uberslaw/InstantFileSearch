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
    /// <summary>
    /// UTC of the latest snapshot that contained this path. Unset when the
    /// caller did not stamp history (folder-contents browse).
    /// </summary>
    public DateTime LastPresentUtc { get; init; }

    public SearchHit WithLastPresent(DateTime completedUtc) => new()
    {
        Name = Name,
        FullPath = FullPath,
        Size = Size,
        Modified = Modified,
        IsFolder = IsFolder,
        Folder = Folder,
        File = File,
        LastPresentUtc = completedUtc,
    };

    public static SearchHit FromFolder(FolderNode folder, DateTime lastPresentUtc = default)
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
            LastPresentUtc = lastPresentUtc,
        };
    }

    public static SearchHit FromFile(FileEntry file, DateTime lastPresentUtc = default)
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
            LastPresentUtc = lastPresentUtc,
        };
    }
}
