// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Windows;
using Foundry.Connect.Services.Localization;
using Foundry.Localization;
using Foundry.Connect.Services.ApplicationLifetime;
using Foundry.Connect.ViewModels;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly ILocalizationService _localization;
    private readonly IApplicationLifetimeService _applicationLifetimeService;
    private readonly ILogger<MainWindow> _logger;

    public MainWindow(
        MainWindowViewModel viewModel,
        IApplicationLifetimeService applicationLifetimeService,
        ILogger<MainWindow> logger,
        ILocalizationService localization)
    {
        _viewModel = viewModel;
        _applicationLifetimeService = applicationLifetimeService;
        _logger = logger;
        InitializeComponent();
        _localization = localization;
        LocalizationRoot.Apply(this, _localization.CurrentCulture);
        _localization.LanguageChanged += OnLanguageChanged;
        DataContext = viewModel;
        Loaded += OnLoadedAsync;
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs args)
    {
        if (Dispatcher.HasShutdownStarted) return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => LocalizationRoot.Apply(this, _localization.CurrentCulture));
            return;
        }
        LocalizationRoot.Apply(this, _localization.CurrentCulture);
    }
    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        _logger.LogInformation("MainWindow loaded. Starting asynchronous initialization.");

        try
        {
            await _viewModel.InitializeAsync();
            _logger.LogInformation("MainWindow asynchronous initialization finished.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MainWindow asynchronous initialization failed.");
            _applicationLifetimeService.Exit(FoundryConnectExitCode.StartupFailure);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _localization.LanguageChanged -= OnLanguageChanged;
        base.OnClosed(e);
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
