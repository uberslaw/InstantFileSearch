using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
    private readonly IFileDeleter _deleter;
    private readonly bool _elevated;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private FolderExclusionSet _exclusions;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _searchCts;
    private int _scanGeneration;
    private int _searchGeneration;
    private readonly List<ScanResult> _scans = [];
    private readonly string _settingsPath;
    private TreeSortMode _treeSort;
    private bool _openingFolderResult;
    private string _scanPath = "";
    private string _searchText = "";
    private string _sizeFromText = "";
    private string _sizeToText = "";
    private string _sizeFromUnit = "MB";
    private string _sizeToUnit = "MB";
    private string _modifiedFromText = "";
    private string _modifiedToText = "";
    private string _searchScope = "All scans";
    private string _matchMode = "Name or path";
    private bool _isAdvancedOpen;
    private string _statusText = "Choose a folder and scan. Drop a folder on the window to start. UNC shares can be pasted in the path box.";
    private bool _isScanning;
    private bool _isEditMode;
    private FolderNode? _selectedFolder;
    private EntryRow? _selectedEntry;
    private int _filesScanned;
    private int _foldersScanned;
    private string _progressPath = "";
    private ObservableCollection<EntryRow> _items = [];

    public MainViewModel(
        string? cachePath = null,
        string? exclusionsPath = null,
        IByteProtector? protector = null,
        IFileDeleter? deleter = null,
        bool? elevated = null)
    {
        _protector = protector ?? ByteProtector.CreateDefault();
        _deleter = deleter ?? new RecycleBinFileDeleter();
        _elevated = elevated ?? ProcessElevation.IsCurrentProcessElevated();
        _cachePath = string.IsNullOrWhiteSpace(cachePath) ? ScanCache.DefaultFilePath : cachePath;
        _exclusionsPath = string.IsNullOrWhiteSpace(exclusionsPath) ? ExclusionStore.DefaultFilePath : exclusionsPath;
        _settingsPath = UiSettingsStore.DefaultFilePath;
        _exclusions = ExclusionStore.Load(_exclusionsPath, _protector);
        _treeSort = UiSettingsStore.Load(_settingsPath).TreeSort;
        BrowseCommand = new RelayCommand(Browse, () => !IsScanning);
        ScanCommand = new RelayCommand(async () => await ScanAsync(), () => !IsScanning);
        CancelCommand = new RelayCommand(Cancel, () => IsScanning);
        OpenCommand = new RelayCommand(OpenSelected, () => SelectedEntry is not null);
        ShowInExplorerCommand = new RelayCommand(ShowInExplorer, _ => CanRevealSelection());
        CopyPathCommand = new RelayCommand(CopyPath, _ => CanCopySelection());
        CopyNameCommand = new RelayCommand(CopyName, _ => CanCopySelection());
        ExcludeFolderCommand = new RelayCommand(ExcludeSelectedFolder, _ => CanExcludeSelectedFolder());
        RemoveScanCommand = new RelayCommand(RemoveSelectedScan, CanRemoveSelectedScan);
        RemoveExclusionCommand = new RelayCommand(RemoveSelectedExclusion, () => SelectedExclusion is not null);
        ShowExclusionsCommand = new RelayCommand(ShowExclusions);
        DeleteCommand = new RelayCommand(DeleteSelected, CanDeleteSelected);
        RunAsAdministratorCommand = new RelayCommand(RunAsAdministrator, () => !IsElevated && !IsScanning);
        SyncExclusionPaths();
        if (_elevated)
        {
            _statusText = "Running as administrator. Admin shares such as \\\\SERVER\\C$ can be scanned from the path box.";
        }
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
    public ICommand CopyNameCommand { get; }
    public ICommand ExcludeFolderCommand { get; }
    public ICommand RemoveScanCommand { get; }
    public ICommand RemoveExclusionCommand { get; }
    public ICommand ShowExclusionsCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand RunAsAdministratorCommand { get; }

    public bool IsElevated => _elevated;

    public string WindowTitle => ProcessElevation.WindowTitle(IsElevated, IsEditMode);

    public string PrivilegeText => ProcessElevation.PrivilegeLabel(IsElevated);

    public string RunAsAdministratorTip => ProcessElevation.RunAsAdministratorTip(IsElevated);

    public string EditModeBanner => UiInteractionMode.EditBanner;

    public bool IsEditMode
    {
        get => _isEditMode;
        set
        {
            if (SetField(ref _isEditMode, value))
            {
                OnPropertyChanged(nameof(IsViewMode));
                OnPropertyChanged(nameof(WindowTitle));
                ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged();
                StatusText = value
                    ? UiInteractionMode.EditBanner
                    : "View mode. Switch to Edit to delete files from the results list.";
            }
        }
    }

    public bool IsViewMode
    {
        get => !_isEditMode;
        set => IsEditMode = !value;
    }

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

    public bool IsAdvancedOpen
    {
        get => _isAdvancedOpen;
        set => SetField(ref _isAdvancedOpen, value);
    }

    public IReadOnlyList<string> SizeUnitOptions { get; } = ["B", "KB", "MB", "GB", "TB"];

    public IReadOnlyList<string> ScopeOptions { get; } = ["All scans", "Selected folder"];

    public IReadOnlyList<string> MatchModeOptions { get; } = ["Name or path", "Name", "Path"];

    public IReadOnlyList<string> TreeSortOptions => TreeSort.Labels;

    public string TreeSortChoice
    {
        get => TreeSort.Label(_treeSort);
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var mode = TreeSort.Parse(value);
            if (_treeSort == mode)
            {
                return;
            }

            _treeSort = mode;
            OnPropertyChanged();
            RefreshTree(SelectedFolder);
            PersistUiSettings();
        }
    }

    public string SizeFromText
    {
        get => _sizeFromText;
        set
        {
            if (SetField(ref _sizeFromText, value))
            {
                ScheduleItemRefresh();
            }
        }
    }

    public string SizeToText
    {
        get => _sizeToText;
        set
        {
            if (SetField(ref _sizeToText, value))
            {
                ScheduleItemRefresh();
            }
        }
    }

    public string SizeFromUnit
    {
        get => _sizeFromUnit;
        set
        {
            if (SetField(ref _sizeFromUnit, string.IsNullOrWhiteSpace(value) ? "MB" : value))
            {
                ScheduleItemRefresh();
            }
        }
    }

    public string SizeToUnit
    {
        get => _sizeToUnit;
        set
        {
            if (SetField(ref _sizeToUnit, string.IsNullOrWhiteSpace(value) ? "MB" : value))
            {
                ScheduleItemRefresh();
            }
        }
    }

    public string ModifiedFromText
    {
        get => _modifiedFromText;
        set
        {
            if (SetField(ref _modifiedFromText, value))
            {
                ScheduleItemRefresh();
            }
        }
    }

    public string ModifiedToText
    {
        get => _modifiedToText;
        set
        {
            if (SetField(ref _modifiedToText, value))
            {
                ScheduleItemRefresh();
            }
        }
    }

    public string SearchScope
    {
        get => _searchScope;
        set
        {
            if (SetField(ref _searchScope, string.IsNullOrWhiteSpace(value) ? "All scans" : value))
            {
                ScheduleItemRefresh();
            }
        }
    }

    public string MatchMode
    {
        get => _matchMode;
        set
        {
            if (SetField(ref _matchMode, string.IsNullOrWhiteSpace(value) ? "Name or path" : value))
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
                ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged();
                ((RelayCommand)RunAsAdministratorCommand).RaiseCanExecuteChanged();
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
            RaiseSelectionCommands();
            NotifyPresentation();
            if (IsSearchActive && !_openingFolderResult)
            {
                if (IsSelectedFolderScope)
                {
                    ScheduleItemRefresh();
                }

                return;
            }

            ShowFolderContents();
        }
    }

    public EntryRow? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetField(ref _selectedEntry, value))
            {
                RaiseSelectionCommands();
                NotifyPresentation();
                if (IsSearchActive
                    && value is { IsFolder: true, Folder: not null }
                    && !ReferenceEquals(_selectedFolder, value.Folder))
                {
                    SelectedFolder = value.Folder;
                }
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
            if (IsSearchActive)
            {
                var matches = Items.Count == 1 ? "1 match" : $"{Items.Count:N0} matches";
                return $"{places}  ·  {ByteFormatter.ToString(totalSize)}  ·  {matches}";
            }

            return $"{places}  ·  {ByteFormatter.ToString(totalSize)}  ·  {totalFiles:N0} files";
        }
    }

    public string EmptyStateText => ResultsUi.EmptyState(
        _scans.Count > 0,
        IsSearchActive,
        Items.Count,
        filesAtLevel: SelectedFolder?.IsFilesNode == true);

    public bool IsEmptyStateVisible => ResultsUi.ShowEmptyState(Items.Count);

    public string ContentsHeaderText => ResultsUi.ContentsHeader(
        _scans.Count > 0,
        IsSearchActive,
        Items.Count,
        filesAtLevel: SelectedFolder?.IsFilesNode == true,
        folderName: SelectedFolder is null ? null : FolderFilesNode.OwnerName(SelectedFolder));

    public string SelectedDetailsPath => SelectedEntry?.FullPath ?? SelectedFolder?.FullPath ?? "";

    public string SelectedDetailsMeta
    {
        get
        {
            if (SelectedEntry is not null)
            {
                return ResultsUi.DetailsMeta(SelectedEntry.Size, SelectedEntry.Modified, SelectedEntry.IsFolder);
            }

            if (SelectedFolder is not null)
            {
                return ResultsUi.DetailsMeta(
                    SelectedFolder.Size,
                    SelectedFolder.Modified,
                    isFolder: !SelectedFolder.IsFilesNode,
                    isFilesNode: SelectedFolder.IsFilesNode);
            }

            return "";
        }
    }

    public bool HasSelectionDetails => SelectedDetailsPath.Length > 0;

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

        if (string.IsNullOrWhiteSpace(ScanPath))
        {
            Browse();
        }

        if (!LocalPathGuard.TryGetFullPath(ScanPath, out var path))
        {
            if (!string.IsNullOrWhiteSpace(ScanPath))
            {
                StatusText = UncPath.LooksLikeUnc(ScanPath)
                    ? "That is not a usable UNC folder. Use \\\\server\\share or \\\\10.x.x.x\\share (admin shares like C$ need the share name)."
                    : "Choose an existing folder to scan.";
            }

            return;
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
        catch (UnauthorizedAccessException ex)
        {
            if (generation != _scanGeneration)
            {
                return;
            }

            StatusText = ex.Message;
            ProgressPath = "";
        }
        catch (DirectoryNotFoundException ex)
        {
            if (generation != _scanGeneration)
            {
                return;
            }

            StatusText = ex.Message;
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

        if (SelectedEntry is { IsFolder: true, Folder: { } folder })
        {
            OpenFolderResult(folder);
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

    public void ShowInExplorer(object? parameter)
    {
        if (!TryGetRevealPath(parameter, out var path, out var isFolder))
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
                Arguments = ResultsUi.ExplorerArguments(path, isFolder),
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Instant File Search", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void CopyPath(object? parameter)
    {
        var path = ResultsUi.ContextPath(
            SelectedEntry?.FullPath,
            SelectedFolder?.FullPath,
            ResultsUi.IsTreeContext(parameter));
        if (path is null)
        {
            return;
        }

        Clipboard.SetText(path);
        StatusText = "Copied " + path;
    }

    public void CopyName(object? parameter)
    {
        var name = ResultsUi.ContextName(
            SelectedEntry?.Name,
            SelectedFolder?.Name,
            ResultsUi.IsTreeContext(parameter));
        if (name is null)
        {
            return;
        }

        Clipboard.SetText(name);
        StatusText = "Copied " + name;
    }

    private bool CanCopySelection() => SelectedEntry is not null || SelectedFolder is not null;

    private bool CanRevealSelection() => CanCopySelection();

    private FolderNode? TargetFolder(object? parameter)
    {
        if (ResultsUi.IsTreeContext(parameter))
        {
            return SelectedFolder;
        }

        return SelectedEntry is { IsFolder: true, Folder: not null }
            ? SelectedEntry.Folder
            : SelectedFolder;
    }

    private bool CanExcludeSelectedFolder()
    {
        if (IsScanning)
        {
            return false;
        }

        if (SelectedEntry is { IsFolder: true, Folder: not null })
        {
            return FolderFilesNode.CanExclude(SelectedEntry.Folder);
        }

        return FolderFilesNode.CanExclude(SelectedFolder);
    }

    public void ExcludeSelectedFolder(object? parameter)
    {
        var node = TargetFolder(parameter);
        if (node?.IsFilesNode == true)
        {
            StatusText = "FILES is not a folder you can exclude.";
            return;
        }

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

    private bool CanDeleteSelected() =>
        UiInteractionMode.CanDeleteFile(
            IsEditMode,
            IsScanning,
            SelectedEntry is { IsFolder: false, File: not null });

    public void DeleteSelected()
    {
        if (!CanDeleteSelected() || SelectedEntry is not { IsFolder: false, File: not null } entry)
        {
            return;
        }

        if (!LocalPathGuard.TryGetFullPath(entry.FullPath, out var path))
        {
            StatusText = "That path is invalid.";
            return;
        }

        var scan = FindScan(entry.File.Parent)
            ?? _scans.FirstOrDefault(item => IndexedFileDelete.IsIndexedFile(item, path, out _));
        if (scan is null)
        {
            StatusText = "That file is not in the current scan.";
            return;
        }

        var recycle = IndexedFileDelete.UsesRecycleBin(path);
        var confirm = MessageBox.Show(
            IndexedFileDelete.ConfirmMessage(entry.Name, path, recycle),
            WindowTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (!IndexedFileDelete.TryDeleteIndexedFile(scan, path, _deleter, out var updated))
            {
                StatusText = "Could not update the list after delete. Scan that folder again.";
                return;
            }

            var parent = entry.File.Parent;
            ReplaceScan(scan, updated);
            PersistAllScans();
            RefreshTree(parent ?? updated.Root);
            ScheduleItemRefresh();
            NotifyPresentation();
            StatusText = recycle
                ? $"Moved {entry.Name} to the Recycle Bin."
                : $"Deleted {entry.Name}.";
        }
        catch (Exception ex)
        {
            StatusText = "Delete failed: " + ex.Message;
            MessageBox.Show(
                ex.Message,
                WindowTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    public void RunAsAdministrator()
    {
        if (IsElevated)
        {
            StatusText = "Already running as administrator.";
            return;
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            StatusText = "Cannot relaunch: the executable path is unknown.";
            return;
        }

        try
        {
            Process.Start(ProcessElevation.RelaunchStartInfo(exe));
            Application.Current?.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode is 1223 or 1227)
        {
            StatusText = "Administrator launch was cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = "Could not relaunch as administrator: " + ex.Message;
        }
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
        FolderFilesNode.Attach(scan.Root);
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
        NotifyPresentation();
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
                NotifyPresentation();
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
        NotifyPresentation();

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
        foreach (var scan in _scans)
        {
            TreeSort.Apply(scan.Root, _treeSort);
        }

        var roots = TreeSort.OrderRoots(_scans.Select(scan => scan.Root), _treeSort);
        TreeRoots.Clear();
        foreach (var root in roots)
        {
            root.IsExpanded = true;
            TreeRoots.Add(root);
        }

        SelectedFolder = select;
    }

    private void PersistUiSettings()
    {
        var path = _settingsPath;
        var settings = new UiSettings { TreeSort = _treeSort };
        _ = Task.Run(() =>
        {
            try
            {
                UiSettingsStore.Save(settings, path);
            }
            catch
            {
                // Session sort still applies; a settings write must not fail the UI.
            }
        });
    }

    private void OpenFolderResult(FolderNode folder)
    {
        _searchGeneration++;
        _searchCts?.Cancel();
        if (_searchText.Length > 0)
        {
            _searchText = "";
            OnPropertyChanged(nameof(SearchText));
        }

        _openingFolderResult = true;
        try
        {
            SelectedFolder = folder;
            ShowFolderContents();
        }
        finally
        {
            _openingFolderResult = false;
        }
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
        ResultsUi.ShowRemoveFromList(SelectedFolder, IsScanning) && FindScan(SelectedFolder) is not null;

    public void RemoveSelectedScan()
    {
        if (!ResultsUi.ShowRemoveFromList(SelectedFolder, IsScanning))
        {
            return;
        }

        var remaining = ScanCache.WithoutRoot(_scans, SelectedFolder);
        if (remaining.Count == _scans.Count)
        {
            return;
        }

        var name = SelectedFolder!.Name;
        _scans.Clear();
        _scans.AddRange(remaining);
        RefreshTree(_scans.LastOrDefault()?.Root);
        PersistAllScans();
        ScheduleItemRefresh();
        NotifyPresentation();
        StatusText = _scans.Count == 0
            ? "Removed from list. Tree is empty."
            : $"Removed {name} from the list. {_scans.Count} location(s) remain.";
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

    private bool TryGetRevealPath(object? parameter, out string fullPath, out bool isFolder)
    {
        fullPath = "";
        isFolder = true;
        if (ResultsUi.IsTreeContext(parameter))
        {
            isFolder = true;
            return LocalPathGuard.TryValidateOpenPath(
                ResultsUi.TreeExplorerPath(SelectedFolder), _scans, out fullPath);
        }

        if (SelectedEntry is not null)
        {
            isFolder = SelectedEntry.IsFolder;
            return TryGetSafeOpenPath(out fullPath);
        }

        isFolder = true;
        return LocalPathGuard.TryValidateOpenPath(
            ResultsUi.TreeExplorerPath(SelectedFolder), _scans, out fullPath);
    }

    private bool IsSearchActive => BuildSearchQuery().HasCriteria;

    private bool IsSelectedFolderScope =>
        string.Equals(SearchScope, "Selected folder", StringComparison.Ordinal);

    private SearchQuery BuildSearchQuery() => new()
    {
        Text = SearchText,
        MinSizeBytes = ByteFormatter.ParseOptionalBytes(SizeFromText, ByteFormatter.ParseUnit(SizeFromUnit)),
        MaxSizeBytes = ByteFormatter.ParseOptionalBytes(SizeToText, ByteFormatter.ParseUnit(SizeToUnit)),
        ModifiedFrom = SearchQuery.ParseDate(ModifiedFromText),
        ModifiedTo = SearchQuery.ParseDate(ModifiedToText),
        UnderFolder = IsSelectedFolderScope ? SelectedFolder?.FullPath ?? "" : null,
        DirectChildrenOnly = IsSelectedFolderScope && SelectedFolder?.IsFilesNode == true,
        Match = MatchMode switch
        {
            "Name" => SearchMatchMode.Name,
            "Path" => SearchMatchMode.Path,
            _ => SearchMatchMode.NameOrPath,
        },
    };

    private void ScheduleItemRefresh()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;

        var query = BuildSearchQuery();
        if (!query.HasCriteria || _scans.Count == 0)
        {
            _searchGeneration++;
            ShowFolderContents();
            return;
        }

        var generation = ++_searchGeneration;
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        _ = SearchAsync(generation, query, cts.Token);
    }

    private async Task SearchAsync(int generation, SearchQuery query, CancellationToken token)
    {
        try
        {
            await Task.Delay(SearchDebounce, token);
            var files = FolderFilesNode.FilesForSearch(
                    _scans.SelectMany(scan => scan.AllFiles),
                    SelectedFolder,
                    IsSelectedFolderScope)
                .ToList();
            var folders = FolderFilesNode.FoldersForSearch(
                    _scans.Select(scan => scan.Root),
                    SelectedFolder,
                    IsSelectedFolderScope)
                .ToList();
            if ((files.Count == 0 && folders.Count == 0) || generation != _searchGeneration)
            {
                return;
            }

            var rows = await Task.Run(() =>
            {
                var matches = FileNameSearch.FilterHits(folders, files, query);
                return matches.Select(ToRow).ToList();
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
                StatusText = FormatSearchStatus(query, rows.Count);
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string FormatSearchStatus(SearchQuery query, int count)
    {
        var label = string.IsNullOrWhiteSpace(query.Text) ? "filters" : $"\"{query.Text.Trim()}\"";
        return count == FileNameSearch.DefaultLimit
            ? $"Showing first {FileNameSearch.DefaultLimit:N0} matches for {label}"
            : $"{count:N0} matches for {label}";
    }

    private void ShowFolderContents()
    {
        if (SelectedFolder is null)
        {
            ReplaceItems([]);
            return;
        }

        var parentSize = SelectedFolder.Size;
        var folders = FolderFilesNode.ContentFolders(SelectedFolder);
        var files = FolderFilesNode.ContentFiles(SelectedFolder);
        var rows = new List<EntryRow>(folders.Count + files.Count);
        foreach (var folder in folders)
        {
            rows.Add(ToFolderRow(folder, parentSize));
        }

        foreach (var file in files)
        {
            rows.Add(ToFileRow(file, parentSize));
        }

        ReplaceItems(rows);

        if (_scans.Count > 0 && !IsScanning && !IsSearchActive)
        {
            StatusText = SelectedFolder.IsFilesNode
                ? $"{files.Count:N0} files in {FolderFilesNode.OwnerName(SelectedFolder)}"
                : $"{folders.Count:N0} folders, {files.Count:N0} files in {SelectedFolder.Name}";
        }
    }

    private void ReplaceItems(IReadOnlyList<EntryRow> rows)
    {
        SelectedEntry = null;
        Items = new ObservableCollection<EntryRow>(rows);
        NotifyPresentation();
    }

    private void NotifyPresentation()
    {
        OnPropertyChanged(nameof(EmptyStateText));
        OnPropertyChanged(nameof(IsEmptyStateVisible));
        OnPropertyChanged(nameof(ContentsHeaderText));
        OnPropertyChanged(nameof(SelectedDetailsPath));
        OnPropertyChanged(nameof(SelectedDetailsMeta));
        OnPropertyChanged(nameof(HasSelectionDetails));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(LastScanText));
    }

    private void RaiseSelectionCommands()
    {
        ((RelayCommand)OpenCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ShowInExplorerCommand).RaiseCanExecuteChanged();
        ((RelayCommand)CopyPathCommand).RaiseCanExecuteChanged();
        ((RelayCommand)CopyNameCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ExcludeFolderCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RemoveScanCommand).RaiseCanExecuteChanged();
        ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged();
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

    private static EntryRow ToRow(SearchHit hit)
    {
        if (hit.IsFolder && hit.Folder is not null)
        {
            return ToFolderRow(hit.Folder, hit.Folder.Parent?.Size ?? hit.Size);
        }

        var file = hit.File ?? throw new InvalidOperationException("File search hit is missing File.");
        return ToFileRow(file, file.Parent?.Size ?? file.Size);
    }

    private static EntryRow ToFolderRow(FolderNode folder, long parentSize) => new()
    {
        Name = folder.Name,
        FullPath = folder.FullPath,
        Kind = "Folder",
        IsFolder = true,
        Size = folder.Size,
        Modified = folder.Modified,
        Percent = parentSize <= 0 ? 0 : folder.Size * 100.0 / parentSize,
        Folder = folder,
    };

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
