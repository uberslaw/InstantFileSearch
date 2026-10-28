using System.Windows;
using System.Windows.Input;

namespace InstantFileSearch;

public partial class ScanHistoryWindow : Window
{
    public ScanHistoryWindow()
    {
        InitializeComponent();
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void HistoryGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        Vm?.LoadSelectedHistory();
    }
}
