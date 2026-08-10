// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Foundry.Connect.ViewModels;

namespace Foundry.Connect.Views;

public partial class DiscoveredWifiList : UserControl
{
    public static readonly DirectProperty<DiscoveredWifiList, MainWindowViewModel?> ViewModelProperty =
        AvaloniaProperty.RegisterDirect<DiscoveredWifiList, MainWindowViewModel?>(
            nameof(ViewModel),
            control => control.ViewModel);

    private MainWindowViewModel? _viewModel;
    private TextBlock? _heading;
    private bool _isAttached;

    public DiscoveredWifiList()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public MainWindowViewModel? ViewModel
    {
        get => _viewModel;
        private set => SetAndRaise(ViewModelProperty, ref _viewModel, value);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_isAttached && ViewModel is not null)
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        ViewModel = DataContext as MainWindowViewModel;
        if (_isAttached && ViewModel is not null)
        {
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
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

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _isAttached = false;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnHeadingAttached(object? sender, VisualTreeAttachmentEventArgs e) =>
        _heading = (TextBlock)sender!;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedWifiNetwork) && ViewModel?.SelectedWifiNetwork is null)
        {
            Dispatcher.UIThread.Post(() => _heading?.Focus());
        }
    }
}
