using System.ComponentModel;
using System.Windows.Controls;
using Foundry.ViewModels;

namespace Foundry.Views;

public partial class NetworkSettingsView : UserControl
{
    private NetworkSettingsViewModel? _viewModel;

    public NetworkSettingsView()
    {
        InitializeComponent();
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as NetworkSettingsViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        SyncWifiPassphraseBox();
    }

    private void WifiPassphraseBox_OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not PasswordBox)
        {
            return;
        }

        SyncWifiPassphraseBox();
    }

    private void WifiPassphraseBox_OnPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not NetworkSettingsViewModel viewModel ||
            sender is not PasswordBox passwordBox)
        {
            return;
        }

        if (!string.Equals(viewModel.WifiPassphrase, passwordBox.Password, StringComparison.Ordinal))
        {
            viewModel.WifiPassphrase = passwordBox.Password;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(NetworkSettingsViewModel.WifiPassphrase), StringComparison.Ordinal))
        {
            SyncWifiPassphraseBox();
        }
    }

    private void SyncWifiPassphraseBox()
    {
        if (_viewModel is null)
        {
            return;
        }

        if (!string.Equals(WifiPassphraseBox.Password, _viewModel.WifiPassphrase, StringComparison.Ordinal))
        {
            WifiPassphraseBox.Password = _viewModel.WifiPassphrase;
        }
    }
}
