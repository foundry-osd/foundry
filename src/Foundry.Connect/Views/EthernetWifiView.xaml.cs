using System.ComponentModel;
using System.Windows.Controls;
using Foundry.Connect.ViewModels;

namespace Foundry.Connect.Views;

public partial class EthernetWifiView : UserControl
{
    private MainWindowViewModel? _viewModel;

    public EthernetWifiView()
    {
        InitializeComponent();
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        SyncSelectedWifiPassphraseBox();
    }

    private void SelectedWifiPassphraseBox_OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not PasswordBox)
        {
            return;
        }

        SyncSelectedWifiPassphraseBox();
    }

    private void SelectedWifiPassphraseBox_OnPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_viewModel is null || sender is not PasswordBox passwordBox)
        {
            return;
        }

        if (!string.Equals(_viewModel.SelectedWifiPassphrase, passwordBox.Password, StringComparison.Ordinal))
        {
            _viewModel.SelectedWifiPassphrase = passwordBox.Password;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(MainWindowViewModel.SelectedWifiPassphrase), StringComparison.Ordinal))
        {
            SyncSelectedWifiPassphraseBox();
        }
    }

    private void SyncSelectedWifiPassphraseBox()
    {
        if (_viewModel is null)
        {
            return;
        }

        if (!string.Equals(SelectedWifiPassphraseBox.Password, _viewModel.SelectedWifiPassphrase, StringComparison.Ordinal))
        {
            SelectedWifiPassphraseBox.Password = _viewModel.SelectedWifiPassphrase;
        }
    }
}
