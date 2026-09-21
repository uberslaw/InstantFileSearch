using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace InstantFileSearch;

public partial class MergeFoldersWindow : Window
{
    public MergeFoldersWindow()
    {
        InitializeComponent();
    }

    public required IReadOnlyList<MergeFolderChoice> Folders { get; init; }

    public Func<string, string, CollisionPolicy, MergePlanResult>? Preview { get; init; }

    public string SourcePath
    {
        get => SourceBox.Text;
        set => SourceBox.Text = value ?? "";
    }

    public string DestPath
    {
        get => DestBox.Text;
        set => DestBox.Text = value ?? "";
    }

    public CollisionPolicy Policy =>
        OverwriteRadio.IsChecked == true ? CollisionPolicy.Overwrite : CollisionPolicy.SkipExisting;

    public MergePlan? Plan { get; private set; }

    public bool DestNeedsScan { get; private set; }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        FolderList.ItemsSource = Folders;
        RefreshSummary();
        if (string.IsNullOrWhiteSpace(DestPath))
        {
            DestBox.Focus();
        }
        else if (string.IsNullOrWhiteSpace(SourcePath))
        {
            SourceBox.Focus();
        }
    }

    private void Path_TextChanged(object sender, TextChangedEventArgs e) => RefreshSummary();

    private void Policy_Changed(object sender, RoutedEventArgs e) => RefreshSummary();

    private void FolderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FolderList.SelectedItem is not MergeFolderChoice choice)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(DestPath) || DestBox.IsKeyboardFocusWithin)
        {
            DestPath = choice.FullPath;
        }
        else if (string.IsNullOrWhiteSpace(SourcePath) || SourceBox.IsKeyboardFocusWithin)
        {
            SourcePath = choice.FullPath;
        }
        else
        {
            DestPath = choice.FullPath;
        }
    }

    private void FolderList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FolderList.SelectedItem is MergeFolderChoice choice)
        {
            DestPath = choice.FullPath;
        }
    }

    private void RefreshSummary()
    {
        if (Preview is null || SummaryText is null)
        {
            return;
        }

        var result = Preview(SourcePath, DestPath, Policy);
        Plan = result.Plan;
        DestNeedsScan = result.DestNeedsScan;
        if (result.Plan is not null)
        {
            SummaryText.Text = IndexedFileMerge.ConfirmMessage(result.Plan);
            return;
        }

        SummaryText.Text = string.IsNullOrWhiteSpace(result.Error)
            ? "Pick a source folder and a destination already in the scan index."
            : result.Error!;
    }

    private void Merge_Click(object sender, RoutedEventArgs e)
    {
        RefreshSummary();
        if (Plan is not null)
        {
            DestNeedsScan = false;
            DialogResult = true;
            return;
        }

        if (DestNeedsScan)
        {
            DialogResult = true;
            return;
        }

        MessageBox.Show(
            string.IsNullOrWhiteSpace(SummaryText.Text)
                ? "Choose a source and a destination from the scan index."
                : SummaryText.Text,
            Title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
