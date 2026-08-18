// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Views;

public sealed partial class WifiPage : Page
{
    public NetworkConfigurationViewModel ViewModel { get; }

    public WifiPage()
    {
        ViewModel = App.GetService<NetworkConfigurationViewModel>();
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.Dispose();
    }

    private void WifiPassphraseBox_OnLoaded(object sender, RoutedEventArgs e) => SyncWifiPassphraseBox();

    private void WifiPassphraseBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox &&
            !string.Equals(ViewModel.WifiPassphrase, passwordBox.Password, StringComparison.Ordinal))
        {
            ViewModel.WifiPassphrase = passwordBox.Password;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(NetworkConfigurationViewModel.WifiPassphrase), StringComparison.Ordinal))
        {
            SyncWifiPassphraseBox();
        }
    }

    private void SyncWifiPassphraseBox()
    {
        if (!string.Equals(WifiPassphraseBox.Password, ViewModel.WifiPassphrase, StringComparison.Ordinal))
        {
            WifiPassphraseBox.Password = ViewModel.WifiPassphrase;
        }
    }
}
