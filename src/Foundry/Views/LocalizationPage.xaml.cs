namespace Foundry.Views;

public sealed partial class LocalizationPage : Page
{
    public LocalizationConfigurationViewModel ViewModel { get; }

    public LocalizationPage()
    {
        ViewModel = App.GetService<LocalizationConfigurationViewModel>();
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        ViewModel.Dispose();
    }
}
