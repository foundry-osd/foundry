using Foundry.Core.Services.Application;

namespace Foundry.Views;

public sealed partial class AutopilotPage : Page
{
    private readonly IExternalProcessLauncher externalProcessLauncher;

    public AutopilotConfigurationViewModel ViewModel { get; }

    public AutopilotPage()
    {
        ViewModel = App.GetService<AutopilotConfigurationViewModel>();
        externalProcessLauncher = App.GetService<IExternalProcessLauncher>();
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

    private void ProfilesTable_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is WinUI.TableView.TableView tableView)
        {
            ViewModel.ReplaceSelectedProfiles(tableView.SelectedItems.OfType<AutopilotProfileEntryViewModel>());
        }
    }

    private async void DocumentationButton_Click(object sender, RoutedEventArgs e)
    {
        await externalProcessLauncher.OpenUriAsync(new Uri(FoundryApplicationInfo.AutopilotDocumentationUrl));
    }

    private void CertificatesTable_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is WinUI.TableView.TableView tableView)
        {
            ViewModel.ReplaceSelectedCertificate(tableView.SelectedItems.OfType<AutopilotCertificateEntryViewModel>());
        }
    }

    private void BootMediaCertificatePasswordBox_OnLoaded(object sender, RoutedEventArgs e)
    {
        SyncBootMediaCertificatePasswordBox();
    }

    private void BootMediaCertificatePasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox passwordBox ||
            string.Equals(ViewModel.GetBootMediaCertificatePassword(), passwordBox.Password, StringComparison.Ordinal))
        {
            return;
        }

        ViewModel.SetBootMediaCertificatePassword(passwordBox.Password);
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
        if (!string.Equals(BootMediaCertificatePasswordBox.Password, ViewModel.GetBootMediaCertificatePassword(), StringComparison.Ordinal))
        {
            BootMediaCertificatePasswordBox.Password = ViewModel.GetBootMediaCertificatePassword();
        }
    }
}
