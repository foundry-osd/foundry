// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Views;

public sealed partial class AutopilotZeroTouchPage : Page
{
    public AutopilotConfigurationViewModel ViewModel { get; }

    public AutopilotZeroTouchPage()
    {
        ViewModel = App.GetService<AutopilotConfigurationViewModel>();
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

    private async void OnModeActionClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.ToggleProvisioningModeAsync(AutopilotProvisioningMode.HardwareHashUpload);
    }
    private void CertificatesTable_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is WinUI.TableView.TableView tableView)
        {
            ViewModel.ReplaceSelectedCertificate(tableView.SelectedItems.OfType<AutopilotCertificateEntryViewModel>());
        }
    }

    private void BootMediaCertificatePasswordBox_OnLoaded(object sender, RoutedEventArgs e) =>
        SyncBootMediaCertificatePasswordBox();

    private void BootMediaCertificatePasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox &&
            !string.Equals(ViewModel.GetBootMediaCertificatePassword(), passwordBox.Password, StringComparison.Ordinal))
        {
            ViewModel.SetBootMediaCertificatePassword(passwordBox.Password);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(AutopilotConfigurationViewModel.BootMediaCertificatePfxPath), StringComparison.Ordinal))
        {
            SyncBootMediaCertificatePasswordBox();
        }
    }

    private void SyncBootMediaCertificatePasswordBox()
    {
        string password = ViewModel.GetBootMediaCertificatePassword();
        if (!string.Equals(BootMediaCertificatePasswordBox.Password, password, StringComparison.Ordinal))
        {
            BootMediaCertificatePasswordBox.Password = password;
        }
    }

}
