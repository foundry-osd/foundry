using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Foundry.Connect.ViewModels;

namespace Foundry.Connect.Views;

public partial class EthernetWifiView : UserControl
{
    private MainWindowViewModel? _viewModel;
    private string? _selectedWifiNetworkSsid;
    private bool _isSelectedWifiPassphraseRevealed;
    private bool _isSyncingSelectedWifiEditors;

    public EthernetWifiView()
    {
        InitializeComponent();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
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

        _selectedWifiNetworkSsid = _viewModel?.SelectedWifiNetwork?.Ssid;
        _isSelectedWifiPassphraseRevealed = false;
        SyncSelectedWifiEditors();
    }

    private void SelectedWifiPassphraseBox_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox)
        {
            return;
        }

        SyncSelectedWifiEditors();
    }

    private void SelectedWifiPassphraseRevealTextBox_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox)
        {
            return;
        }

        SyncSelectedWifiEditors();
    }

    private void SelectedWifiPassphraseBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncingSelectedWifiEditors ||
            _viewModel is null ||
            sender is not PasswordBox passwordBox)
        {
            return;
        }

        if (!string.Equals(_viewModel.SelectedWifiPassphrase, passwordBox.Password, StringComparison.Ordinal))
        {
            _viewModel.SelectedWifiPassphrase = passwordBox.Password;
        }
    }

    private void SelectedWifiPassphraseRevealTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingSelectedWifiEditors ||
            _viewModel is null ||
            sender is not TextBox textBox)
        {
            return;
        }

        if (!string.Equals(_viewModel.SelectedWifiPassphrase, textBox.Text, StringComparison.Ordinal))
        {
            _viewModel.SelectedWifiPassphrase = textBox.Text;
        }
    }

    private void SelectedWifiRevealButton_OnClick(object sender, RoutedEventArgs e)
    {
        _isSelectedWifiPassphraseRevealed = !_isSelectedWifiPassphraseRevealed;
        SyncSelectedWifiEditors();

        if (sender is not DependencyObject dependencyObject)
        {
            return;
        }

        ListViewItem? item = FindAncestor<ListViewItem>(dependencyObject);
        if (item is null)
        {
            return;
        }

        if (_isSelectedWifiPassphraseRevealed)
        {
            TextBox? textBox = FindDescendant<TextBox>(item, "SelectedWifiPassphraseRevealTextBox");
            if (textBox is null)
            {
                return;
            }

            textBox.Focus();
            textBox.CaretIndex = textBox.Text.Length;
            return;
        }

        PasswordBox? passwordBox = FindDescendant<PasswordBox>(item, "SelectedWifiPassphraseBox");
        passwordBox?.Focus();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (string.Equals(e.PropertyName, nameof(MainWindowViewModel.SelectedWifiNetwork), StringComparison.Ordinal))
        {
            string? selectedWifiNetworkSsid = _viewModel.SelectedWifiNetwork?.Ssid;

            if (!string.Equals(_selectedWifiNetworkSsid, selectedWifiNetworkSsid, StringComparison.OrdinalIgnoreCase))
            {
                _isSelectedWifiPassphraseRevealed = false;
            }

            _selectedWifiNetworkSsid = selectedWifiNetworkSsid;
            SyncSelectedWifiEditors();
            return;
        }

        if (string.Equals(e.PropertyName, nameof(MainWindowViewModel.SelectedWifiPassphrase), StringComparison.Ordinal))
        {
            if (string.IsNullOrEmpty(_viewModel.SelectedWifiPassphrase))
            {
                _isSelectedWifiPassphraseRevealed = false;
            }

            SyncSelectedWifiEditors();
            return;
        }

        if (string.Equals(e.PropertyName, nameof(MainWindowViewModel.IsCompactViewport), StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, nameof(MainWindowViewModel.IsWideViewport), StringComparison.Ordinal))
        {
            SyncSelectedWifiEditors();
        }
    }

    private void SyncSelectedWifiEditors()
    {
        if (_viewModel is null)
        {
            return;
        }

        SyncSelectedWifiEditors(CompactWifiNetworksListView);
        SyncSelectedWifiEditors(WideWifiNetworksListView);
    }

    private void SyncSelectedWifiEditors(ListView? listView)
    {
        if (_viewModel is null ||
            listView is null ||
            _viewModel.SelectedWifiNetwork is null)
        {
            return;
        }

        if (listView.ItemContainerGenerator.ContainerFromItem(_viewModel.SelectedWifiNetwork) is not ListViewItem item)
        {
            return;
        }

        PasswordBox? passwordBox = FindDescendant<PasswordBox>(item, "SelectedWifiPassphraseBox");
        TextBox? textBox = FindDescendant<TextBox>(item, "SelectedWifiPassphraseRevealTextBox");

        if (passwordBox is null && textBox is null)
        {
            return;
        }

        _isSyncingSelectedWifiEditors = true;

        try
        {
            string passphrase = _viewModel.SelectedWifiPassphrase;

            if (passwordBox is not null &&
                !string.Equals(passwordBox.Password, passphrase, StringComparison.Ordinal))
            {
                passwordBox.Password = passphrase;
            }

            if (textBox is not null &&
                !string.Equals(textBox.Text, passphrase, StringComparison.Ordinal))
            {
                textBox.Text = passphrase;
            }

            if (passwordBox is not null)
            {
                passwordBox.Visibility = _isSelectedWifiPassphraseRevealed
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }

            if (textBox is not null)
            {
                textBox.Visibility = _isSelectedWifiPassphraseRevealed
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }
        finally
        {
            _isSyncingSelectedWifiEditors = false;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? dependencyObject)
        where T : DependencyObject
    {
        while (dependencyObject is not null)
        {
            if (dependencyObject is T typedObject)
            {
                return typedObject;
            }

            dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject? dependencyObject, string elementName)
        where T : FrameworkElement
    {
        if (dependencyObject is null)
        {
            return null;
        }

        int childCount = VisualTreeHelper.GetChildrenCount(dependencyObject);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(dependencyObject, index);
            if (child is T element &&
                string.Equals(element.Name, elementName, StringComparison.Ordinal))
            {
                return element;
            }

            T? descendant = FindDescendant<T>(child, elementName);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }
}
