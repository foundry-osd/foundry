using System.ComponentModel;
using System.Windows;
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

        ApplyMode(_viewModel.IsAdvancedEnabled);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
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
        ContentFrame.Navigate(isAdvancedEnabled ? _advancedPage : _standardPage);

        if (isAdvancedEnabled)
        {
            Width = AdvancedWidth;
            Height = AdvancedHeight;
            return;
        }

        Width = StandardWidth;
        Height = StandardHeight;
    }
}
