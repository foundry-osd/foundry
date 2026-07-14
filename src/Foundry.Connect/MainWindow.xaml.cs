// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Foundry.Connect.Models.Configuration;
using Foundry.Connect.Services.ApplicationLifetime;
using Foundry.Connect.ViewModels;
using Foundry.Core.Services.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IApplicationLifetimeService _applicationLifetimeService;
    private readonly FoundryConnectConfiguration _configuration;
    private readonly ILogger<MainWindow> _logger;

    public MainWindow(
        MainWindowViewModel viewModel,
        IApplicationLifetimeService applicationLifetimeService,
        FoundryConnectConfiguration configuration,
        ILogger<MainWindow> logger)
    {
        _viewModel = viewModel;
        _applicationLifetimeService = applicationLifetimeService;
        _configuration = configuration;
        _logger = logger;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoadedAsync;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // PreviewKeyDown is used so the shortcut still works while a text box has focus.
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (TroubleshootingConsole.IsShortcut(
                _configuration.TroubleshootingConsole,
                key.ToString(),
                Keyboard.Modifiers.HasFlag(ModifierKeys.Control),
                Keyboard.Modifiers.HasFlag(ModifierKeys.Alt),
                Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)))
        {
            _logger.LogInformation("Troubleshooting console shortcut pressed. Opening an interactive console.");
            if (!TroubleshootingConsole.TryLaunch())
            {
                _logger.LogWarning("Failed to open the troubleshooting console.");
            }

            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        _logger.LogInformation("MainWindow loaded. Starting asynchronous initialization.");

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
