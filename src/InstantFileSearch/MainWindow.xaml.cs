using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace InstantFileSearch;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Loaded += async (_, _) =>
        {
            SearchBox.Focus();
            await Vm.RestoreLastScanAsync();
        };
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        Vm.SelectedFolder = e.NewValue as FolderNode;
    }

    private void FolderTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindTreeViewItem(e.OriginalSource as DependencyObject) is { } item)
        {
            item.IsSelected = true;
            e.Handled = false;
        }
    }

    private static TreeViewItem? FindTreeViewItem(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is TreeViewItem item)
            {
                return item;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void EntryGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        Vm.OpenSelected();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        var canDrop = !Vm.IsScanning && e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = canDrop ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (Vm.IsScanning)
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }

        if (!LocalPathGuard.TryGetFullPath(paths[0], out var dropped))
        {
            return;
        }

        var path = dropped;
        if (File.Exists(path))
        {
            path = Path.GetDirectoryName(path) ?? path;
        }

        if (!LocalPathGuard.TryResolveExistingDirectory(path, out var directory))
        {
            return;
        }

        Vm.ScanPath = directory;
        await Vm.ScanAsync();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F3 ||
            (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control))
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && EntryGrid.IsKeyboardFocusWithin)
        {
            Vm.OpenSelected();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.C
            && Keyboard.Modifiers == ModifierKeys.Control
            && Keyboard.FocusedElement is not TextBox)
        {
            if (FolderTree.IsKeyboardFocusWithin)
            {
                Vm.CopyPath(ResultsUi.TreeContext);
                e.Handled = true;
            }
            else if (EntryGrid.IsKeyboardFocusWithin)
            {
                Vm.CopyPath(null);
                e.Handled = true;
            }
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
