// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Foundry.Connect.ViewModels;

namespace Foundry.Connect.Views;

/// <summary>
/// The single Connect page. The code-behind is framework glue only: it copies the password out of the
/// <see cref="Foundry.Wpf.Controls.PassphraseEditor"/> into the view model (the password is not bindable), empties the
/// editor when the view model clears the password, keeps Enter on the Wi-Fi list from reaching the window's default
/// button, and moves the keyboard focus when the page asks for it.
/// </summary>
public partial class ConnectPage : UserControl
{
    private ConnectPageViewModel? _viewModel;

    public ConnectPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        PassphraseInput.PassphraseChanged += OnPassphraseChanged;
        WifiNetworksList.PreviewKeyDown += OnWifiNetworksListPreviewKeyDown;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        Attach(e.NewValue as ConnectPageViewModel);

    private void OnLoaded(object sender, RoutedEventArgs e) => Attach(DataContext as ConnectPageViewModel);

    private void OnUnloaded(object sender, RoutedEventArgs e) => Attach(null);

    private void Attach(ConnectPageViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.FocusRequested -= OnFocusRequested;
            _viewModel.WifiNetworks.PassphraseCleared -= OnPassphraseCleared;
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.FocusRequested += OnFocusRequested;
            _viewModel.WifiNetworks.PassphraseCleared += OnPassphraseCleared;
        }
    }

    // The view model keeps its own buffer; the copy handed out by the editor is wiped as soon as it is passed on.
    private void OnPassphraseChanged(object? sender, EventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        char[] copy = PassphraseInput.GetPassphraseCopy();
        try
        {
            _viewModel.WifiNetworks.SetPassphrase(copy);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(copy.AsSpan()));
        }
    }

    private void OnPassphraseCleared(object? sender, EventArgs e) => PassphraseInput.Clear();

    // An unhandled Enter would reach the default button (Continue) and leave for Deploy; on the list it chooses the focused
    // row instead.
    private void OnWifiNetworksListPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        if (!e.IsRepeat &&
            _viewModel is not null &&
            e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(WifiNetworksList, source) is ListBoxItem { DataContext: WifiNetworkItemViewModel network })
        {
            _viewModel.WifiNetworks.ChooseNetwork(network);
        }
    }

    // Queued at input priority so the bindings of the new status (visibility, enabled state) apply first.
    private void OnFocusRequested(object? sender, ConnectFocusTarget target) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => MoveFocus(target));

    private void MoveFocus(ConnectFocusTarget target)
    {
        FrameworkElement? element = target switch
        {
            ConnectFocusTarget.Continue => ContinueButton,
            ConnectFocusTarget.Retry => RetryButton,
            ConnectFocusTarget.ProvisionedConnect => ProvisionedConnectButton,
            ConnectFocusTarget.WifiNetworks => ResolveWifiListFocusTarget(),
            _ => null
        };

        if (element is null || !element.IsVisible)
        {
            return;
        }

        element.Focus();
        element.BringIntoView();
    }

    // The selected row keeps the arrow-key position; without a selection the list itself takes the focus.
    private FrameworkElement ResolveWifiListFocusTarget() =>
        WifiNetworksList.SelectedItem is { } selected &&
        WifiNetworksList.ItemContainerGenerator.ContainerFromItem(selected) is FrameworkElement container
            ? container
            : WifiNetworksList;
}
