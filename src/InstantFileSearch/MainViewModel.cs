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
    public static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(200);

    private readonly FileScanner _scanner = new();
    private readonly string _cachePath;
    private readonly string _exclusionsPath;
    private readonly IByteProtector _protector;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private FolderExclusionSet _exclusions;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _searchCts;
    private int _scanGeneration;
    private int _searchGeneration;
    private readonly List<ScanResult> _scans = [];
    private string _scanPath = "";
    private string _searchText = "";
    private string _statusText = "Choose a folder and scan. Drop a folder on the window to start.";
    private bool _isScanning;
    private FolderNode? _selectedFolder;
    private EntryRow? _selectedEntry;
    private int _filesScanned;
    private int _foldersScanned;
    private string _progressPath = "";
    private ObservableCollection<EntryRow> _items = [];

    public MainViewModel(string? cachePath = null, string? exclusionsPath = null, IByteProtector? protector = null)
    {
        _protector = protector ?? ByteProtector.CreateDefault();
        _cachePath = string.IsNullOrWhiteSpace(cachePath) ? ScanCache.DefaultFilePath : cachePath;
        _exclusionsPath = string.IsNullOrWhiteSpace(exclusionsPath) ? ExclusionStore.DefaultFilePath : exclusionsPath;
        _exclusions = ExclusionStore.Load(_exclusionsPath, _protector);
        BrowseCommand = new RelayCommand(Browse, () => !IsScanning);
        ScanCommand = new RelayCommand(async () => await ScanAsync(), () => !IsScanning);
        CancelCommand = new RelayCommand(Cancel, () => IsScanning);
        OpenCommand = new RelayCommand(OpenSelected, () => SelectedEntry is not null);
        ShowInExplorerCommand = new RelayCommand(ShowInExplorer, () => SelectedEntry is not null);
        CopyPathCommand = new RelayCommand(CopyPath, () => SelectedEntry is not null);
        ExcludeFolderCommand = new RelayCommand(ExcludeSelectedFolder, CanExcludeSelectedFolder);
        RemoveScanCommand = new RelayCommand(RemoveSelectedScan, CanRemoveSelectedScan);
        RemoveExclusionCommand = new RelayCommand(RemoveSelectedExclusion, () => SelectedExclusion is not null);
        ShowExclusionsCommand = new RelayCommand(ShowExclusions);
        SyncExclusionPaths();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<FolderNode> TreeRoots { get; } = [];

    public ObservableCollection<EntryRow> Items
    {
        get => _items;
        private set => SetField(ref _items, value);
    }

    public ObservableCollection<string> ExclusionPaths { get; } = [];

    public ICommand BrowseCommand { get; }
    public ICommand ScanCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand ShowInExplorerCommand { get; }
    public ICommand CopyPathCommand { get; }
    public ICommand ExcludeFolderCommand { get; }
    public ICommand RemoveScanCommand { get; }
    public ICommand RemoveExclusionCommand { get; }
    public ICommand ShowExclusionsCommand { get; }

    public string? SelectedExclusion
    {
        get => _selectedExclusion;
        set
        {
            if (SetField(ref _selectedExclusion, value))
            {
                ((RelayCommand)RemoveExclusionCommand).RaiseCanExecuteChanged();
            }
        }
    }

    private string? _selectedExclusion;

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
                ScheduleItemRefresh();
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
                ((RelayCommand)ExcludeFolderCommand).RaiseCanExecuteChanged();
                ((RelayCommand)RemoveScanCommand).RaiseCanExecuteChanged();
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
            ((RelayCommand)ExcludeFolderCommand).RaiseCanExecuteChanged();
            ((RelayCommand)RemoveScanCommand).RaiseCanExecuteChanged();
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                ShowFolderContents();
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
                ((RelayCommand)ExcludeFolderCommand).RaiseCanExecuteChanged();
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

            if (_scans.Count == 0)
            {
                return "No scan yet";
            }

            var totalSize = _scans.Sum(scan => scan.Root.Size);
            var totalFiles = _scans.Sum(scan => scan.Root.FileCount);
            var places = _scans.Count == 1 ? "1 location" : $"{_scans.Count} locations";
            return $"{places}  ·  {ByteFormatter.ToString(totalSize)}  ·  {totalFiles:N0} files";
        }
    }

    public string LastScanText
    {
        get
        {
            var latest = _scans.OrderByDescending(scan => scan.CompletedUtc).FirstOrDefault();
            return latest is null || latest.CompletedUtc == default
                ? ""
                : "Last scan " + latest.CompletedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
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
            var result = await Task.Run(() => _scanner.Scan(path, progress, token, _exclusions.Items), token);
            if (generation != _scanGeneration)
            {
                return;
            }

            ApplyCompletedScan(result, persist: true, restored: false);
        }
        catch (OperationCanceledException)
        {
            if (generation != _scanGeneration)
            {
                return;
            }

            StatusText = _scans.Count == 0
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

    private bool CanExcludeSelectedFolder()
    {
        if (IsScanning)
        {
            return false;
        }

        if (SelectedEntry is { IsFolder: true, Folder.Parent: not null })
        {
            return true;
        }

        return SelectedFolder?.Parent is not null;
    }

    public void ExcludeSelectedFolder()
    {
        var node = SelectedEntry is { IsFolder: true, Folder: not null }
            ? SelectedEntry.Folder
            : SelectedFolder;
        if (node?.Parent is null)
        {
            StatusText = "The scan root cannot be excluded.";
            return;
        }

        if (!_exclusions.Add(node.FullPath))
        {
            StatusText = "That folder is already excluded.";
            return;
        }

        ExclusionStore.Save(_exclusions, _exclusionsPath, _protector);
        SyncExclusionPaths();
        PruneExcludedFolder(node);
        StatusText = $"Excluded {node.Name}. Future scans skip it too.";
    }

    public void RemoveSelectedExclusion()
    {
        if (SelectedExclusion is null)
        {
            return;
        }

        _exclusions.Remove(SelectedExclusion);
        ExclusionStore.Save(_exclusions, _exclusionsPath, _protector);
        SyncExclusionPaths();
        SelectedExclusion = null;
        StatusText = "Exclusion removed. Scan again to include that folder.";
    }

    public void ShowExclusions()
    {
        var window = new ExclusionsWindow
        {
            Owner = Application.Current?.MainWindow,
            DataContext = this,
        };
        window.ShowDialog();
    }

    private void SyncExclusionPaths()
    {
        ExclusionPaths.Clear();
        foreach (var path in _exclusions.Items)
        {
            ExclusionPaths.Add(path);
        }
    }

    private void PruneExcludedFolder(FolderNode node)
    {
        if (FindScan(node) is not { } scan || node.Parent is null)
        {
            return;
        }

        var parent = node.Parent;
        parent.Folders.Remove(node);
        for (var walk = parent; walk is not null; walk = walk.Parent)
        {
            walk.Size = Math.Max(0, walk.Size - node.Size);
            walk.FileCount = Math.Max(0, walk.FileCount - node.FileCount);
            walk.FolderCount = Math.Max(0, walk.FolderCount - 1 - node.FolderCount);
        }

        var remaining = scan.AllFiles
            .Where(file => !LocalPathGuard.IsSameOrUnder(file.FullPath, node.FullPath))
            .ToList();
        ReplaceScan(scan, new ScanResult
        {
            Root = scan.Root,
            AllFiles = remaining,
            Duration = scan.Duration,
            ErrorCount = scan.ErrorCount,
            CompletedUtc = scan.CompletedUtc,
        });

        RefreshTree(parent);
        PersistAllScans();
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(LastScanText));
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

    public Task RestoreLastScanAsync()
    {
        if (IsScanning || _scans.Count > 0)
        {
            return Task.CompletedTask;
        }

        return Task.Run(() =>
        {
            if (!ScanCache.TryLoadAll(_cachePath, out var cached, _protector) || cached.Count == 0)
            {
                return;
            }

            PostToUi(() =>
            {
                if (IsScanning || _scans.Count > 0)
                {
                    return;
                }

                foreach (var scan in cached)
                {
                    UpsertScan(scan);
                }

                RefreshTree(cached[^1].Root);
                ScanPath = cached[^1].Root.FullPath;
                ProgressPath = "";
                OnPropertyChanged(nameof(LastScanText));
                OnPropertyChanged(nameof(SummaryText));
                StatusText = cached.Count == 1
                    ? $"Restored {cached[0].Root.Name}."
                    : $"Restored {cached.Count} scans.";
            });
        });
    }

    private void ApplyCompletedScan(ScanResult result, bool persist, bool restored)
    {
        UpsertScan(result);
        result.Root.IsExpanded = true;
        RefreshTree(result.Root);
        ScanPath = result.Root.FullPath;
        ProgressPath = "";
        OnPropertyChanged(nameof(LastScanText));
        OnPropertyChanged(nameof(SummaryText));

        var when = FormatScanTime(result.CompletedUtc);
        var elapsed = ScanLocation.FormatDuration(result.Duration);
        StatusText = restored
            ? $"Restored {result.Root.Name} from {when} ({elapsed}). Scan again to refresh."
            : $"Scan complete in {elapsed} at {when}. {_scans.Count} location(s) in the tree.";

        if (persist)
        {
            PersistAllScans();
            try
            {
                ScanLog.Append(result);
            }
            catch
            {
                // Cache save is the source of truth; a log write must not fail the scan.
            }
        }
    }

    private void UpsertScan(ScanResult result)
    {
        var next = ScanCache.Upsert(_scans, result);
        _scans.Clear();
        _scans.AddRange(next);
    }

    private void ReplaceScan(ScanResult previous, ScanResult updated)
    {
        var index = _scans.IndexOf(previous);
        if (index >= 0)
        {
            _scans[index] = updated;
        }
    }

    private void RefreshTree(FolderNode? select)
    {
        TreeRoots.Clear();
        foreach (var scan in _scans)
        {
            scan.Root.IsExpanded = true;
            TreeRoots.Add(scan.Root);
        }

        SelectedFolder = select;
    }

    private ScanResult? FindScan(FolderNode? node)
    {
        var root = node;
        while (root?.Parent is not null)
        {
            root = root.Parent;
        }

        return root is null ? null : _scans.FirstOrDefault(scan => ReferenceEquals(scan.Root, root));
    }

    private bool CanRemoveSelectedScan() =>
        !IsScanning && SelectedFolder is { Parent: null } && FindScan(SelectedFolder) is not null;

    public void RemoveSelectedScan()
    {
        if (SelectedFolder is not { Parent: null } || FindScan(SelectedFolder) is not { } scan)
        {
            return;
        }

        _scans.Remove(scan);
        RefreshTree(_scans.LastOrDefault()?.Root);
        PersistAllScans();
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(LastScanText));
        StatusText = _scans.Count == 0
            ? "Removed scan. Tree is empty."
            : $"Removed {scan.Root.Name}. {_scans.Count} location(s) remain.";
    }

    private void PersistAllScans()
    {
        var path = _cachePath;
        var snapshot = _scans.ToList();
        _ = Task.Run(() =>
        {
            try
            {
                ScanCache.SaveAll(snapshot, path, _protector);
            }
            catch
            {
                PostToUi(() =>
                {
                    if (!IsScanning)
                    {
                        StatusText = "Scan complete, but saving the cache failed. Results are only in this session.";
                    }
                });
            }
        });
    }

    private bool TryGetSafeOpenPath(out string fullPath) =>
        LocalPathGuard.TryValidateOpenPath(SelectedEntry?.FullPath, _scans, out fullPath);

    private void ScheduleItemRefresh()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;

        if (string.IsNullOrWhiteSpace(SearchText) || _scans.Count == 0)
        {
            _searchGeneration++;
            ShowFolderContents();
            return;
        }

        var generation = ++_searchGeneration;
        var query = SearchText;
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        _ = SearchAsync(generation, query, cts.Token);
    }

    private async Task SearchAsync(int generation, string query, CancellationToken token)
    {
        try
        {
            await Task.Delay(SearchDebounce, token);
            var files = _scans.SelectMany(scan => scan.AllFiles).ToList();
            if (files.Count == 0 || generation != _searchGeneration)
            {
                return;
            }

            var rows = await Task.Run(() =>
            {
                var matches = FileNameSearch.Filter(files, query);
                return matches.Select(file => ToFileRow(file, file.Parent?.Size ?? file.Size)).ToList();
            }, token);

            if (generation != _searchGeneration)
            {
                return;
            }

            PostToUi(() =>
            {
                if (generation != _searchGeneration)
                {
                    return;
                }

                ReplaceItems(rows);
                StatusText = rows.Count == FileNameSearch.DefaultLimit
                    ? $"Showing first {FileNameSearch.DefaultLimit:N0} matches for \"{query.Trim()}\""
                    : $"{rows.Count:N0} files match \"{query.Trim()}\"";
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ShowFolderContents()
    {
        if (SelectedFolder is null)
        {
            ReplaceItems([]);
            return;
        }

        var parentSize = SelectedFolder.Size;
        var rows = new List<EntryRow>(SelectedFolder.Folders.Count + SelectedFolder.Files.Count);
        foreach (var folder in SelectedFolder.Folders)
        {
            rows.Add(new EntryRow
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
            rows.Add(ToFileRow(file, parentSize));
        }

        ReplaceItems(rows);

        if (_scans.Count > 0 && !IsScanning && string.IsNullOrWhiteSpace(SearchText))
        {
            StatusText = $"{SelectedFolder.Folders.Count:N0} folders, {SelectedFolder.Files.Count:N0} files in {SelectedFolder.Name}";
        }
    }

    private void ReplaceItems(IReadOnlyList<EntryRow> rows)
    {
        SelectedEntry = null;
        Items = new ObservableCollection<EntryRow>(rows);
    }

    private void PostToUi(Action action)
    {
        if (_ui is null || SynchronizationContext.Current == _ui)
        {
            action();
            return;
        }

        _ui.Post(_ => action(), null);
    }

    private static string FormatScanTime(DateTime utc) =>
        (utc == default ? DateTime.UtcNow : utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm");

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
