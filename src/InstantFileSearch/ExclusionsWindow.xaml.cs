using System.Windows;

namespace InstantFileSearch;

public partial class ExclusionsWindow : Window
{
    public ExclusionsWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
