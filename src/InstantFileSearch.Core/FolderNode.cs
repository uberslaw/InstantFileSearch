using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace InstantFileSearch;

public sealed class FolderNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;

    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public FolderNode? Parent { get; set; }
    public List<FolderNode> Folders { get; } = [];
    public List<FileEntry> Files { get; } = [];
    public long Size { get; set; }
    public int FileCount { get; set; }
    public int FolderCount { get; set; }
    public DateTime Modified { get; set; }
    public ScanLocationKind LocationKind { get; set; }
    public TimeSpan ScanDuration { get; set; }

    public string DurationText =>
        Parent is null && ScanDuration > TimeSpan.Zero
            ? ScanLocation.FormatDuration(ScanDuration)
            : "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public double PercentOfParent =>
        Parent is null || Parent.Size <= 0 ? 100 : Size * 100.0 / Parent.Size;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public void ExpandAncestors()
    {
        for (var node = Parent; node is not null; node = node.Parent)
        {
            node.IsExpanded = true;
        }
    }

    private bool SetField(ref bool field, bool value, [CallerMemberName] string? name = null)
    {
        if (field == value)
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
