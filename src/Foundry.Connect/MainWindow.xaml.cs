using System.ComponentModel;
using System.Windows;
using Foundry.Connect.Models;
using Foundry.Connect.Services.ApplicationLifetime;
using Foundry.Connect.ViewModels;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IApplicationLifetimeService _applicationLifetimeService;
    private readonly ILogger<MainWindow> _logger;

    public MainWindow(
        MainWindowViewModel viewModel,
        IApplicationLifetimeService applicationLifetimeService,
        ILogger<MainWindow> logger)
    {
        _viewModel = viewModel;
        _applicationLifetimeService = applicationLifetimeService;
        _logger = logger;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoadedAsync;
        SizeChanged += OnWindowSizeChanged;
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        _logger.LogInformation("MainWindow loaded. Starting asynchronous initialization.");
        _viewModel.UpdateViewport(ActualWidth, ActualHeight);

        try
        {
            await _viewModel.InitializeAsync();
            _logger.LogInformation("MainWindow asynchronous initialization completed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MainWindow asynchronous initialization failed.");
            _applicationLifetimeService.Exit(FoundryConnectExitCode.StartupFailure);
        }
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _viewModel.UpdateViewport(e.NewSize.Width, e.NewSize.Height);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _logger.LogInformation("MainWindow closing. IsExitRequested={IsExitRequested}.", _applicationLifetimeService.IsExitRequested);

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.HandleWindowClosing();
        }

        base.OnClosing(e);
    }
}
