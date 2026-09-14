using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace InstantFileSearch;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly FileScanner _scanner = new();
    private CancellationTokenSource? _scanCts;
    private ScanResult? _result;
    private string _scanPath = "";
    private string _searchText = "";
    private string _statusText = "Choose a folder and scan. Drop a folder on the window to start.";
    private bool _isScanning;
    private FolderNode? _selectedFolder;
    private EntryRow? _selectedEntry;
    private int _filesScanned;
    private int _foldersScanned;
    private string _progressPath = "";

    public MainViewModel()
    {
        BrowseCommand = new RelayCommand(Browse, () => !IsScanning);
        ScanCommand = new RelayCommand(async () => await ScanAsync(), () => !IsScanning);
        CancelCommand = new RelayCommand(Cancel, () => IsScanning);
        OpenCommand = new RelayCommand(OpenSelected, () => SelectedEntry is not null);
        ShowInExplorerCommand = new RelayCommand(ShowInExplorer, () => SelectedEntry is not null);
        CopyPathCommand = new RelayCommand(CopyPath, () => SelectedEntry is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<FolderNode> TreeRoots { get; } = [];
    public ObservableCollection<EntryRow> Items { get; } = [];

    public ICommand BrowseCommand { get; }
    public ICommand ScanCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand ShowInExplorerCommand { get; }
    public ICommand CopyPathCommand { get; }

    public string ScanPath
    {
        get => _scanPath;
        set => SetField(ref _scanPath, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
            {
                RefreshItems();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public string ProgressPath
    {
        get => _progressPath;
        set => SetField(ref _progressPath, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            if (SetField(ref _isScanning, value))
            {
                ((RelayCommand)BrowseCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ScanCommand).RaiseCanExecuteChanged();
                ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsIdle => !IsScanning;

    public FolderNode? SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            if (SetField(ref _selectedFolder, value) && string.IsNullOrWhiteSpace(SearchText))
            {
                RefreshItems();
            }
        }
    }

    public EntryRow? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetField(ref _selectedEntry, value))
            {
                ((RelayCommand)OpenCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ShowInExplorerCommand).RaiseCanExecuteChanged();
                ((RelayCommand)CopyPathCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public int FilesScanned
    {
        get => _filesScanned;
        set => SetField(ref _filesScanned, value);
    }

    public int FoldersScanned
    {
        get => _foldersScanned;
        set => SetField(ref _foldersScanned, value);
    }

    public string SummaryText
    {
        get
        {
            if (_result is null)
            {
                return IsScanning
                    ? $"{ByteFormatter.ToString(_bytesScanned)}  ·  {FilesScanned:N0} files  ·  {FoldersScanned:N0} folders"
                    : "No scan yet";
            }

            var errors = _result.ErrorCount == 0 ? "" : $"  ·  {_result.ErrorCount:N0} skipped";
            return $"{ByteFormatter.ToString(_result.Root.Size)}  ·  {_result.Root.FileCount:N0} files  ·  {_result.Root.FolderCount:N0} folders  ·  {_result.Duration.TotalSeconds:0.0}s{errors}";
        }
    }

    private long _bytesScanned;

    public async Task ScanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ScanPath))
        {
            Browse();
            if (string.IsNullOrWhiteSpace(ScanPath))
            {
                return;
            }
        }

        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        OnPropertyChanged(nameof(IsIdle));
        TreeRoots.Clear();
        Items.Clear();
        _result = null;
        SelectedFolder = null;
        FilesScanned = 0;
        FoldersScanned = 0;
        _bytesScanned = 0;
        StatusText = "Scanning…";
        OnPropertyChanged(nameof(SummaryText));

        var progress = new Progress<ScanProgress>(p =>
        {
            FilesScanned = p.Files;
            FoldersScanned = p.Folders;
            _bytesScanned = p.Bytes;
            ProgressPath = p.CurrentPath;
            StatusText = $"Scanning {p.CurrentPath}";
            OnPropertyChanged(nameof(SummaryText));
        });

        try
        {
            var path = ScanPath;
            var result = await Task.Run(() => _scanner.Scan(path, progress, _scanCts.Token), _scanCts.Token);
            _result = result;
            TreeRoots.Add(result.Root);
            SelectedFolder = result.Root;
            ProgressPath = "";
            StatusText = $"Scan complete. Type in Search to filter {result.AllFiles.Count:N0} files instantly.";
            RefreshItems();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
            ProgressPath = "";
        }
        catch (Exception ex)
        {
            StatusText = "Scan failed: " + ex.Message;
            ProgressPath = "";
        }
        finally
        {
            IsScanning = false;
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public void Cancel() => _scanCts?.Cancel();

    public void OpenSelected()
    {
        if (SelectedEntry is null)
        {
            return;
        }

        if (SelectedEntry.IsFolder && SelectedEntry.Folder is not null && string.IsNullOrWhiteSpace(SearchText))
        {
            SelectedFolder = SelectedEntry.Folder;
            return;
        }

        TryStart(SelectedEntry.FullPath);
    }

    public void ShowInExplorer()
    {
        if (SelectedEntry is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = SelectedEntry.IsFolder
                ? $"\"{SelectedEntry.FullPath}\""
                : $"/select,\"{SelectedEntry.FullPath}\"",
            UseShellExecute = true,
        });
    }

    public void CopyPath()
    {
        if (SelectedEntry is not null)
        {
            Clipboard.SetText(SelectedEntry.FullPath);
            StatusText = "Copied " + SelectedEntry.FullPath;
        }
    }

    private void Browse()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder to scan",
        };
        if (!string.IsNullOrWhiteSpace(ScanPath) && System.IO.Directory.Exists(ScanPath))
        {
            dialog.InitialDirectory = ScanPath;
        }

        if (dialog.ShowDialog() == true)
        {
            ScanPath = dialog.FolderName;
        }
    }

    private void RefreshItems()
    {
        Items.Clear();

        if (!string.IsNullOrWhiteSpace(SearchText) && _result is not null)
        {
            var matches = FileNameSearch.Filter(_result.AllFiles, SearchText).ToList();
            foreach (var file in matches)
            {
                Items.Add(ToFileRow(file, _result.Root.Size));
            }

            StatusText = matches.Count == 5000
                ? $"Showing first 5,000 matches for \"{SearchText.Trim()}\""
                : $"{matches.Count:N0} files match \"{SearchText.Trim()}\"";
            return;
        }

        if (SelectedFolder is null)
        {
            return;
        }

        var parentSize = SelectedFolder.Size;
        foreach (var folder in SelectedFolder.Folders)
        {
            Items.Add(new EntryRow
            {
                Name = folder.Name,
                FullPath = folder.FullPath,
                Kind = "Folder",
                IsFolder = true,
                Size = folder.Size,
                Modified = folder.Modified,
                Percent = parentSize <= 0 ? 0 : folder.Size * 100.0 / parentSize,
                Folder = folder,
            });
        }

        foreach (var file in SelectedFolder.Files)
        {
            Items.Add(ToFileRow(file, parentSize));
        }

        if (_result is not null && !IsScanning)
        {
            StatusText = $"{SelectedFolder.Folders.Count:N0} folders, {SelectedFolder.Files.Count:N0} files in {SelectedFolder.Name}";
        }
    }

    private static EntryRow ToFileRow(FileEntry file, long parentSize) => new()
    {
        Name = file.Name,
        FullPath = file.FullPath,
        Kind = "File",
        IsFolder = false,
        Size = file.Size,
        Modified = file.Modified,
        Percent = parentSize <= 0 ? 0 : file.Size * 100.0 / parentSize,
        File = file,
    };

    private static void TryStart(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Instant File Search", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
