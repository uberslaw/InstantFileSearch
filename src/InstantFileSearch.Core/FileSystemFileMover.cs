namespace InstantFileSearch;

/// <summary>
/// <see cref="File.Move(string, string, bool)"/> on the same volume is a rename.
/// Cross-volume uses copy + delete so IFS never asks Explorer to prepare a tree.
/// </summary>
public sealed class FileSystemFileMover : IFileMover
{
    public void EnsureDirectory(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        Directory.CreateDirectory(directoryPath);
    }

    public bool FileExists(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        return File.Exists(fullPath);
    }

    public void Move(string sourcePath, string destPath, bool overwrite)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destPath);
        File.Move(sourcePath, destPath, overwrite);
    }

    public void CopyThenDelete(string sourcePath, string destPath, bool overwrite)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destPath);
        File.Copy(sourcePath, destPath, overwrite);
        File.Delete(sourcePath);
    }
}
