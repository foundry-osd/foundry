// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Foundry.Connect.Lifetime;
using Foundry.Connect.Startup;
using Foundry.Core.Models.Runtime;
using Foundry.Core.Services.Runtime;
using Foundry.Connect.ViewModels;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IApplicationLifetimeService _applicationLifetimeService;
    private readonly ILogger<MainWindow> _logger;
    private readonly RuntimeStartupDiagnostics _startup;
    private readonly TaskCompletionSource _rendered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _closed;

    public MainWindow(
        MainWindowViewModel viewModel,
        IApplicationLifetimeService applicationLifetimeService,
        ILogger<MainWindow> logger,
        RuntimeStartupDiagnostics startup)
    {
        _viewModel = viewModel;
        _applicationLifetimeService = applicationLifetimeService;
        _logger = logger;
        _startup = startup;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoadedAsync;
        ContentRendered += (_, _) => _rendered.TrySetResult();
        Closed += (_, _) => { _closed = true; _rendered.TrySetCanceled(); };
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedAsync;
        _logger.LogInformation("MainWindow loaded. Starting asynchronous initialization.");

        try
        {
            await _startup.ObserveInitializationAsync(_viewModel.InitializeAsync, WaitForRenderedAsync,
                () => !_closed && _applicationLifetimeService.RequestedExitCode is null);
            _logger.LogInformation("MainWindow asynchronous initialization finished.");
        }
        catch (OperationCanceledException) when (_closed || _applicationLifetimeService.RequestedExitCode is not null)
        {
        }
        catch (Exception ex)
        {
            _startup.ReportFailure(ex, "ui_initialization");
            _applicationLifetimeService.TryExit(ConnectStartupFailurePolicy.Map(ConnectStartupStage.UiStartup));
        }
    }

    private async Task WaitForRenderedAsync()
    {
        await _rendered.Task;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _logger.LogInformation("MainWindow closing. RequestedExitCode={RequestedExitCode}.", _applicationLifetimeService.RequestedExitCode);

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.HandleWindowClosing();
        }

        base.OnClosing(e);
    }
}
