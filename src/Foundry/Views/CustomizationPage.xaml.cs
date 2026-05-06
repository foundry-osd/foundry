namespace Foundry.Views;

public sealed partial class CustomizationPage : Page
{
    public CustomizationConfigurationViewModel ViewModel { get; }

    public CustomizationPage()
    {
        ViewModel = App.GetService<CustomizationConfigurationViewModel>();
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        ViewModel.Dispose();
    }
}
