// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Foundry.Connect.Controls;
using Foundry.Connect.ViewModels;

namespace Foundry.Connect.Views;

public partial class DiscoveredWifiRow : UserControl
{
    public static readonly StyledProperty<MainWindowViewModel.WifiNetworkItemViewModel?> NetworkProperty =
        AvaloniaProperty.Register<DiscoveredWifiRow, MainWindowViewModel.WifiNetworkItemViewModel?>(nameof(Network));

    public static readonly StyledProperty<MainWindowViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<DiscoveredWifiRow, MainWindowViewModel?>(nameof(ViewModel));

    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<DiscoveredWifiRow, bool>(nameof(IsSelected));

    private WifiPassphraseEditor? _passphraseEditor;
    private Button? _connectButton;
    private Button? _disconnectButton;
    private bool _isAttached;

    public DiscoveredWifiRow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public MainWindowViewModel.WifiNetworkItemViewModel? Network
    {
        get => GetValue(NetworkProperty);
        set => SetValue(NetworkProperty, value);
    }

    public MainWindowViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ViewModelProperty)
        {
            if (_isAttached && change.OldValue is MainWindowViewModel oldViewModel)
            {
                oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            if (_isAttached && change.NewValue is MainWindowViewModel newViewModel)
            {
                newViewModel.PropertyChanged += OnViewModelPropertyChanged;
            }
        }
        else if (change.Property == IsSelectedProperty && change.GetNewValue<bool>())
        {
            Dispatcher.UIThread.Post(FocusPrimaryAction);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        if (ViewModel is not null)
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _isAttached = false;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnPassphraseEditorAttached(object? sender, VisualTreeAttachmentEventArgs e) =>
        _passphraseEditor = (WifiPassphraseEditor)sender!;

    private void OnConnectButtonAttached(object? sender, VisualTreeAttachmentEventArgs e) =>
        _connectButton = (Button)sender!;

    private void OnDisconnectButtonAttached(object? sender, VisualTreeAttachmentEventArgs e) =>
        _disconnectButton = (Button)sender!;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsSelected && e.PropertyName == nameof(MainWindowViewModel.SelectedWifiActionFeedbackText) &&
            ViewModel?.HasSelectedWifiActionFeedback == true)
        {
            Dispatcher.UIThread.Post(FocusPrimaryAction);
        }
    }

    private void FocusPrimaryAction()
    {
        if (!IsSelected || Network is null)
        {
            return;
        }

        if (Network.RequiresPassphrase && !Network.IsConnected)
        {
            _passphraseEditor?.FocusEditor();
        }
        else if (Network.IsConnected)
        {
            _disconnectButton?.Focus();
        }
        else
        {
            _connectButton?.Focus();
        }
    }
}
