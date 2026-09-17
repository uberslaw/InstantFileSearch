using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace InstantFileSearch;

/// <summary>
/// Windows Recycle Bin for local files. UNC and non-Windows fall back to
/// <see cref="File.Delete"/> (the Recycle Bin does not keep network files).
/// </summary>
public sealed class RecycleBinFileDeleter : IFileDeleter
{
    public void Delete(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        if (IndexedFileDelete.UsesRecycleBin(fullPath))
        {
            FileSystem.DeleteFile(
                fullPath,
                UIOption.OnlyErrorDialogs,
                RecycleOption.SendToRecycleBin,
                UICancelOption.ThrowException);
            return;
        }

        File.Delete(fullPath);
    }
}
