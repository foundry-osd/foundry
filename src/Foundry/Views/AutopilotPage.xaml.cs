namespace Foundry.Views;

public sealed partial class AutopilotPage : Page
{
    public AutopilotConfigurationViewModel ViewModel { get; }

    public AutopilotPage()
    {
        ViewModel = App.GetService<AutopilotConfigurationViewModel>();
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        ViewModel.Dispose();
    }

    private void ProfilesTable_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is WinUI.TableView.TableView tableView)
        {
            ViewModel.ReplaceSelectedProfiles(tableView.SelectedItems.OfType<AutopilotProfileEntryViewModel>());
        }
    }
}
