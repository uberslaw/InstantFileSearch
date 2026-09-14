namespace InstantFileSearch;

public readonly record struct ScanProgress(
    int Files,
    int Folders,
    long Bytes,
    string CurrentPath);
