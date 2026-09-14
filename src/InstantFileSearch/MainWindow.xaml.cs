using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace InstantFileSearch;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Loaded += (_, _) => SearchBox.Focus();
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        Vm.SelectedFolder = e.NewValue as FolderNode;
    }

    private void EntryGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        Vm.OpenSelected();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
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
        if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
