using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Foundry.ViewModels;
using Foundry.Views;

namespace Foundry;

public partial class MainWindow : Window
{
    private const double StandardWidth = 600;
    private const double StandardHeight = 800;
    private const double AdvancedWidth = 900;
    private const double AdvancedHeight = 600;

    private readonly MainWindowViewModel _viewModel;
    private readonly StandardPage _standardPage;
    private readonly AdvancedPage _advancedPage;
    private bool _isAdvancedEnabled;
    private FrameworkElement? _currentBanner;

    public MainWindow(
        MainWindowViewModel viewModel,
        StandardPage standardPage,
        AdvancedPage advancedPage)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _standardPage = standardPage;
        _advancedPage = advancedPage;

        DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _standardPage.Banner.SizeChanged += OnBannerLayoutChanged;
        _standardPage.Banner.IsVisibleChanged += OnBannerVisibilityChanged;
        _advancedPage.Banner.SizeChanged += OnBannerLayoutChanged;
        _advancedPage.Banner.IsVisibleChanged += OnBannerVisibilityChanged;
        Loaded += OnLoaded;

        ApplyMode(_viewModel.IsAdvancedEnabled);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _standardPage.Banner.SizeChanged -= OnBannerLayoutChanged;
        _standardPage.Banner.IsVisibleChanged -= OnBannerVisibilityChanged;
        _advancedPage.Banner.SizeChanged -= OnBannerLayoutChanged;
        _advancedPage.Banner.IsVisibleChanged -= OnBannerVisibilityChanged;
        Loaded -= OnLoaded;
        base.OnClosed(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsAdvancedEnabled))
        {
            return;
        }

        ApplyMode(_viewModel.IsAdvancedEnabled);
    }

    private void ApplyMode(bool isAdvancedEnabled)
    {
        _isAdvancedEnabled = isAdvancedEnabled;
        ContentFrame.Navigate(isAdvancedEnabled ? _advancedPage : _standardPage);
        _currentBanner = isAdvancedEnabled ? _advancedPage.Banner : _standardPage.Banner;
        UpdateWindowSizeDeferred();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateWindowSizeDeferred();
    }

    private void OnBannerLayoutChanged(object sender, SizeChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _currentBanner))
        {
            return;
        }

        UpdateWindowSizeDeferred();
    }

    private void OnBannerVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _currentBanner))
        {
            return;
        }

        UpdateWindowSizeDeferred();
    }

    private void UpdateWindowSizeDeferred()
    {
        Dispatcher.BeginInvoke(UpdateWindowSize, DispatcherPriority.Loaded);
    }

    private void UpdateWindowSize()
    {
        var baseWidth = _isAdvancedEnabled ? AdvancedWidth : StandardWidth;
        var baseHeight = _isAdvancedEnabled ? AdvancedHeight : StandardHeight;
        var bannerHeight = GetVisibleBannerHeight();

        Width = baseWidth;
        Height = baseHeight + bannerHeight;
    }

    private double GetVisibleBannerHeight()
    {
        if (_currentBanner is null || !_currentBanner.IsVisible)
        {
            return 0;
        }

        return _currentBanner.ActualHeight;
    }
}
