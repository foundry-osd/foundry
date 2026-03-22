using System.Windows;

namespace Foundry.Views;

public partial class AutopilotProfileSelectionDialog : Window
{
    public AutopilotProfileSelectionDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ProfilesListView.Items.Count > 0 && ProfilesListView.SelectedItem is null)
        {
            ProfilesListView.SelectedIndex = 0;
        }

        _ = ProfilesListView.Focus();
    }
}
