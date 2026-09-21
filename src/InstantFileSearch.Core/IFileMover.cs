namespace InstantFileSearch;

/// <summary>
/// Disk transfer for Edit-mode merge. Tests inject a fake; they must not
/// copy or rename real files. Never uses Explorer / IFileOperation.
/// </summary>
public interface IFileMover
{
    void EnsureDirectory(string directoryPath);

    bool FileExists(string fullPath);

    /// <summary>Same-volume rename. Must not copy bytes.</summary>
    void Move(string sourcePath, string destPath, bool overwrite);

    /// <summary>Cross-volume copy, then delete the source.</summary>
    void CopyThenDelete(string sourcePath, string destPath, bool overwrite);
}
