// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
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
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        Opened += OnOpenedAsync;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private async void OnOpenedAsync(object? sender, EventArgs e)
    {
        _logger.LogInformation("MainWindow opened. Starting asynchronous initialization.");

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

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        _logger.LogInformation(
            "MainWindow closing. IsExitRequested={IsExitRequested}.",
            _applicationLifetimeService.IsExitRequested);
        _viewModel.HandleWindowClosing();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Opened -= OnOpenedAsync;
        Closing -= OnClosing;
        Closed -= OnClosed;
    }
}
