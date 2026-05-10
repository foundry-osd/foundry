using System.Windows;
using Foundry.ViewModels;

namespace Foundry;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoadedAsync;
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedAsync;
        _ = _viewModel.RefreshUsbCandidatesCommand.ExecuteAsync(null);
        await _viewModel.RunStartupUpdateCheckAsync();
    }
}
