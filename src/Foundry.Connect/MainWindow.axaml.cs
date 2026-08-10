// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Foundry.Connect.Services.ApplicationLifetime;
using Foundry.Connect.Services.Diagnostics;
using Foundry.Connect.ViewModels;
using Foundry.Connect.Views;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;
    private IApplicationLifetimeService? _applicationLifetimeService;
    private IConnectDiagnosticsSnapshotProvider? _diagnosticsProvider;
    private ILogger<MainWindow>? _logger;
    private MenuItem? _languageMenu;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public MainWindow(
        MainWindowViewModel viewModel,
        IApplicationLifetimeService applicationLifetimeService,
        ILogger<MainWindow> logger)
        : this(viewModel, applicationLifetimeService, logger, diagnosticsProvider: null)
    {
    }

    public MainWindow(
        MainWindowViewModel viewModel,
        IApplicationLifetimeService applicationLifetimeService,
        ILogger<MainWindow> logger,
        IConnectDiagnosticsSnapshotProvider? diagnosticsProvider)
        : this()
    {
        _viewModel = viewModel;
        _applicationLifetimeService = applicationLifetimeService;
        _diagnosticsProvider = diagnosticsProvider;
        _logger = logger;
        DataContext = viewModel;
        _languageMenu = this.FindControl<MenuItem>("LanguageMenu");
        PopulateLanguageMenu();
        viewModel.SupportedCultures.CollectionChanged += OnSupportedCulturesChanged;
        Opened += OnOpenedAsync;
        Closing += OnClosing;
        Closed += OnClosed;
        viewModel.ShowAboutRequested += OnShowAboutRequested;
    }

    private async void OnOpenedAsync(object? sender, EventArgs e)
    {
        if (_viewModel is null || _applicationLifetimeService is null || _logger is null)
        {
            return;
        }

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
        if (_viewModel is null || _applicationLifetimeService is null || _logger is null)
        {
            return;
        }

        _logger.LogInformation(
            "MainWindow closing. IsExitRequested={IsExitRequested}.",
            _applicationLifetimeService.IsExitRequested);
        _viewModel.HandleWindowClosing();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.SupportedCultures.CollectionChanged -= OnSupportedCulturesChanged;
            _viewModel.ShowAboutRequested -= OnShowAboutRequested;
        }

        Opened -= OnOpenedAsync;
        Closing -= OnClosing;
        Closed -= OnClosed;
    }

    private async void OnShowAboutRequested(object? sender, EventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        var viewModel = new AboutDialogViewModel(
            _viewModel.Strings["About.Title"],
            _viewModel.Strings["App.Name"],
            FoundryConnectApplicationInfo.Version,
            _viewModel.Strings["About.DescriptionLine1"],
            _viewModel.Strings["About.DescriptionLine2"],
            _viewModel.Strings["About.Footer"]);
        var dialog = new AboutDialog { DataContext = viewModel };
        EventHandler closeRequested = (_, _) => dialog.Close();
        viewModel.CloseRequested += closeRequested;

        try
        {
            await dialog.ShowDialog(this);
        }
        finally
        {
            viewModel.CloseRequested -= closeRequested;
        }
    }

    private async void OnShowDiagnosticsClicked(object? sender, RoutedEventArgs e)
    {
        if (_diagnosticsProvider is null)
        {
            return;
        }

        var viewModel = new ConnectDiagnosticsDialogViewModel(_diagnosticsProvider);
        var dialog = new ConnectDiagnosticsDialog(viewModel);
        await dialog.ShowDialog(this);
    }

    private void OnSupportedCulturesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        PopulateLanguageMenu();

    private void PopulateLanguageMenu()
    {
        if (_languageMenu is null || _viewModel is null)
        {
            return;
        }

        _languageMenu.Items.Clear();
        foreach (var culture in _viewModel.SupportedCultures)
        {
            _languageMenu.Items.Add(new MenuItem
            {
                Header = culture.DisplayName,
                Command = _viewModel.SetCultureCommand,
                CommandParameter = culture.Code,
                IsChecked = culture.IsSelected,
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "Language"
            });
        }
    }
}
