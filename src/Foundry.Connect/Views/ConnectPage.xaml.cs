// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Foundry.Connect.ViewModels;

namespace Foundry.Connect.Views;

/// <summary>
/// The single Connect page. The code-behind is framework glue only: it copies the password out of the
/// <see cref="Foundry.Wpf.Controls.PassphraseEditor"/> into the view model (the password is not bindable), empties the
/// editor when the view model clears the password, keeps Enter on the Wi-Fi list and on the Ethernet expander from
/// reaching the window's default button, moves the keyboard focus when the page asks for it, and gives the focus back
/// to the same section when WPF lost it because the focused element collapsed or became disabled.
/// </summary>
public partial class ConnectPage : UserControl
{
    private readonly Section[] _sections;
    private ConnectPageViewModel? _viewModel;
    private Window? _window;
    private Section? _lastFocusedSection;
    private IInputElement? _interimFocus;

    public ConnectPage()
    {
        InitializeComponent();
        _sections =
        [
            new Section(EthernetCard, static () => null),
            new Section(ProvisionedCard, () => ProvisionedActionButton),
            new Section(WifiCard, () => WifiActionButton)
        ];

        DataContextChanged += OnDataContextChanged;
        PassphraseInput.PassphraseChanged += OnPassphraseChanged;
        WifiNetworksList.PreviewKeyDown += OnWifiNetworksListPreviewKeyDown;
        EthernetExpander.PreviewKeyDown += OnEthernetExpanderPreviewKeyDown;
        foreach (Section section in _sections)
        {
            section.Root.IsKeyboardFocusWithinChanged += OnSectionKeyboardFocusWithinChanged;
        }

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        Attach(e.NewValue as ConnectPageViewModel);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Attach(DataContext as ConnectPageViewModel);
        SetWindow(Window.GetWindow(this));
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Attach(null);
        SetWindow(null);
    }

    private void Attach(ConnectPageViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.FocusRequested -= OnFocusRequested;
            _viewModel.WifiNetworks.PassphraseCleared -= OnPassphraseCleared;
            _viewModel.ProvisionedWifi.ActionFocusRequested -= OnProvisionedActionFocusRequested;
            _viewModel.WifiNetworks.ActionFocusRequested -= OnWifiActionFocusRequested;
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.FocusRequested += OnFocusRequested;
            _viewModel.WifiNetworks.PassphraseCleared += OnPassphraseCleared;
            _viewModel.ProvisionedWifi.ActionFocusRequested += OnProvisionedActionFocusRequested;
            _viewModel.WifiNetworks.ActionFocusRequested += OnWifiActionFocusRequested;
        }
    }

    private void SetWindow(Window? window)
    {
        if (_window is not null)
        {
            _window.Activated -= OnWindowActivated;
        }

        _window = window;
        if (_window is not null)
        {
            _window.Activated += OnWindowActivated;
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

    // An unhandled Enter would reach the default button (Continue) and leave for Deploy; on the Ethernet expander it toggles
    // the details instead. The details hold no focusable element, so any Enter here comes from the header.
    private void OnEthernetExpanderPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        if (!e.IsRepeat && _viewModel is not null)
        {
            _viewModel.Ethernet.IsDetailsExpanded = !_viewModel.Ethernet.IsDetailsExpanded;
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
            ConnectFocusTarget.ProvisionedConnect => ProvisionedActionButton,
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

    // Remembers the section the keyboard was in; when the focus leaves it, checks after the layout whether WPF dropped it.
    private void OnSectionKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Section? section = Array.Find(_sections, item => ReferenceEquals(item.Root, sender));
        if (section is null)
        {
            return;
        }

        if (e.NewValue is true)
        {
            _lastFocusedSection = section;
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, CheckLostFocus);
    }

    // A dialog that closes (the deployment password prompt) may hand the focus back to an element that collapsed meanwhile.
    private void OnWindowActivated(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, CheckLostFocus);

    private void OnProvisionedActionFocusRequested(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => ReturnFocusToAction(_sections[1]));

    private void OnWifiActionFocusRequested(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => ReturnFocusToAction(_sections[2]));

    private void CheckLostFocus()
    {
        if (_lastFocusedSection is { } section && IsFocusLost())
        {
            FocusFallback(section);
        }
    }

    // When a command of the section ends, its action button takes the focus back from WPF (focus lost) or from the interim
    // target the fallback chose while the button was disabled; any other focus the technician chose stays.
    private void ReturnFocusToAction(Section section)
    {
        if (!IsFocusLost() && !ReferenceEquals(Keyboard.FocusedElement, _interimFocus))
        {
            return;
        }

        if (section.Action() is { IsVisible: true, IsEnabled: true } button)
        {
            _interimFocus = null;
            button.Focus();
            return;
        }

        FocusFallback(section);
    }

    // Order: the section's action button, the section's first focusable element, then the first visible of the Ethernet
    // expander, the Wi-Fi refresh button and Retry. Never Continue or Stay here.
    private void FocusFallback(Section section)
    {
        if (section.Action() is { IsVisible: true, IsEnabled: true } button && button.Focus())
        {
            _interimFocus = button;
            return;
        }

        if (section.Root.IsVisible && section.Root.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)))
        {
            _interimFocus = Keyboard.FocusedElement;
            return;
        }

        Control[] candidates = [EthernetExpander, WifiRefreshButton, RetryButton];
        foreach (Control candidate in candidates)
        {
            if (candidate.IsVisible && candidate.Focus())
            {
                _interimFocus = candidate;
                return;
            }
        }
    }

    // Lost: no keyboard focus, the window itself, or an element of this window outside the page that is not a menu (the
    // menu bar keeps its own focus). Focus in another window, such as a dialog, is not lost.
    private bool IsFocusLost()
    {
        if (Keyboard.FocusedElement is not DependencyObject focused)
        {
            return true;
        }

        if (ReferenceEquals(focused, _window))
        {
            return true;
        }

        if (Window.GetWindow(focused) is { } owner && !ReferenceEquals(owner, _window))
        {
            return false;
        }

        return !IsWithin(focused, this) && !IsInMenu(focused);
    }

    private static bool IsWithin(DependencyObject element, DependencyObject ancestor)
    {
        for (DependencyObject? current = element; current is not null; current = ParentOf(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInMenu(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = ParentOf(current))
        {
            if (current is MenuBase or MenuItem or Popup)
            {
                return true;
            }
        }

        return false;
    }

    private static DependencyObject? ParentOf(DependencyObject element) =>
        (element is Visual ? VisualTreeHelper.GetParent(element) : null) ?? LogicalTreeHelper.GetParent(element);

    private sealed record Section(FrameworkElement Root, Func<Button?> Action);
}
