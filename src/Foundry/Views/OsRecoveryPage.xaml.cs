namespace Foundry.Views;

public sealed partial class OsRecoveryPage : Page
{
    public OsRecoveryConfigurationViewModel ViewModel { get; }

    public OsRecoveryPage()
    {
        ViewModel = App.GetService<OsRecoveryConfigurationViewModel>();
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        ViewModel.Dispose();
    }
}
