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
    private int _scanGeneration;
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
            if (ReferenceEquals(_selectedFolder, value))
            {
                return;
            }

            if (_selectedFolder is not null)
            {
                _selectedFolder.IsSelected = false;
            }

            _selectedFolder = value;
            if (_selectedFolder is not null)
            {
                _selectedFolder.ExpandAncestors();
                _selectedFolder.IsSelected = true;
            }

            OnPropertyChanged();
            if (string.IsNullOrWhiteSpace(SearchText))
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
            if (IsScanning)
            {
                return $"{ByteFormatter.ToString(_bytesScanned)}  ·  {FilesScanned:N0} files  ·  {FoldersScanned:N0} folders";
            }

            if (_result is null)
            {
                return "No scan yet";
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

        if (!LocalPathGuard.TryResolveExistingDirectory(ScanPath, out var path))
        {
            if (string.IsNullOrWhiteSpace(ScanPath))
            {
                Browse();
            }

            if (!LocalPathGuard.TryResolveExistingDirectory(ScanPath, out path))
            {
                if (!string.IsNullOrWhiteSpace(ScanPath))
                {
                    StatusText = "Choose an existing folder to scan.";
                }

                return;
            }
        }

        ScanPath = path;

        var cts = new CancellationTokenSource();
        var previous = _scanCts;
        _scanCts = cts;
        previous?.Dispose();
        var generation = ++_scanGeneration;
        var token = cts.Token;

        IsScanning = true;
        OnPropertyChanged(nameof(IsIdle));
        FilesScanned = 0;
        FoldersScanned = 0;
        _bytesScanned = 0;
        StatusText = "Scanning…";
        OnPropertyChanged(nameof(SummaryText));

        var progress = new Progress<ScanProgress>(p =>
        {
            if (generation != _scanGeneration)
            {
                return;
            }

            FilesScanned = p.Files;
            FoldersScanned = p.Folders;
            _bytesScanned = p.Bytes;
            ProgressPath = p.CurrentPath;
            StatusText = $"Scanning {p.CurrentPath}";
            OnPropertyChanged(nameof(SummaryText));
        });

        try
        {
            var result = await Task.Run(() => _scanner.Scan(path, progress, token), token);
            if (generation != _scanGeneration)
            {
                return;
            }

            ApplyCompletedScan(result);
        }
        catch (OperationCanceledException)
        {
            if (generation != _scanGeneration)
            {
                return;
            }

            StatusText = _result is null
                ? "Scan cancelled."
                : "Scan cancelled. Previous results kept.";
            ProgressPath = "";
        }
        catch (Exception ex)
        {
            if (generation != _scanGeneration)
            {
                return;
            }

            StatusText = "Scan failed: " + ex.Message;
            ProgressPath = "";
        }
        finally
        {
            if (generation == _scanGeneration)
            {
                IsScanning = false;
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(SummaryText));
            }
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

        if (!TryGetSafeOpenPath(out var path))
        {
            MessageBox.Show(
                "That path is not part of the current scan or no longer exists.",
                "Instant File Search",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        TryStart(path);
    }

    public void ShowInExplorer()
    {
        if (SelectedEntry is null)
        {
            return;
        }

        if (!TryGetSafeOpenPath(out var path))
        {
            MessageBox.Show(
                "That path is not part of the current scan or no longer exists.",
                "Instant File Search",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = SelectedEntry.IsFolder
                    ? $"\"{path}\""
                    : $"/select,\"{path}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Instant File Search", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
        if (LocalPathGuard.TryResolveExistingDirectory(ScanPath, out var initial))
        {
            dialog.InitialDirectory = initial;
        }

        if (dialog.ShowDialog() == true &&
            LocalPathGuard.TryResolveExistingDirectory(dialog.FolderName, out var folder))
        {
            ScanPath = folder;
        }
    }

    private void ApplyCompletedScan(ScanResult result)
    {
        _result = result;
        result.Root.IsExpanded = true;
        TreeRoots.Clear();
        TreeRoots.Add(result.Root);
        SelectedFolder = result.Root;
        ProgressPath = "";
        StatusText = $"Scan complete. Type in Search to filter {result.AllFiles.Count:N0} files instantly.";
        RefreshItems();
    }

    private bool TryGetSafeOpenPath(out string fullPath) =>
        LocalPathGuard.TryValidateOpenPath(SelectedEntry?.FullPath, _result, out fullPath);

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

            StatusText = matches.Count == FileNameSearch.DefaultLimit
                ? $"Showing first {FileNameSearch.DefaultLimit:N0} matches for \"{SearchText.Trim()}\""
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
