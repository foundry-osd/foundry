// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Windows;
using Foundry.Connect.Services.ApplicationLifetime;
using Foundry.Connect.ViewModels;
using Foundry.Connect.Views;
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
        Closed += OnClosed;
        _viewModel.ShowAboutRequested += OnShowAboutRequested;
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

    private void OnShowAboutRequested(object? sender, EventArgs e)
    {
        var viewModel = new AboutDialogViewModel(
            _viewModel.Strings["About.Title"],
            _viewModel.Strings["App.Name"],
            FoundryConnectApplicationInfo.Version,
            _viewModel.Strings["About.DescriptionLine1"],
            _viewModel.Strings["About.DescriptionLine2"],
            _viewModel.Strings["About.Footer"]);
        var dialog = new AboutDialog
        {
            DataContext = viewModel,
            Owner = this
        };
        EventHandler closeRequestedHandler = (_, _) => dialog.Close();

        try
        {
            viewModel.CloseRequested += closeRequestedHandler;
            _ = dialog.ShowDialog();
        }
        finally
        {
            viewModel.CloseRequested -= closeRequestedHandler;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.ShowAboutRequested -= OnShowAboutRequested;
        Closed -= OnClosed;
    }
}
