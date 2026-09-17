namespace InstantFileSearch;

/// <summary>
/// Disk delete for Edit mode. Tests inject a fake; they must not touch the real disk.
/// </summary>
public interface IFileDeleter
{
    void Delete(string fullPath);
}
