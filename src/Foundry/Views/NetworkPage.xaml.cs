using Foundry.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Foundry.Views;

public sealed partial class NetworkPage : Page
{
    public NetworkPage()
    {
        InitializeComponent();
    }

    private void OnWifiPasswordChanged(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (DataContext is NetworkSettingsViewModel viewModel &&
            sender is PasswordBox passwordBox &&
            !string.Equals(viewModel.WifiPassphrase, passwordBox.Password, StringComparison.Ordinal))
        {
            viewModel.WifiPassphrase = passwordBox.Password;
        }
    }
}
