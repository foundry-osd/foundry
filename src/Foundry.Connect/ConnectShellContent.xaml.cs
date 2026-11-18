// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Foundry.Connect.Lifetime;
using Foundry.Connect.Startup;
using Foundry.Connect.ViewModels;
using Foundry.Core.Services.Runtime;
using Foundry.Wpf.Shell;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect;

/// <summary>
/// The Foundry Connect page of the WinPE shell window: the menu row, the boot media banner, the version and the network
/// page. It runs the page initialization once it is loaded and reports the phase's readiness once it rendered.
/// </summary>
public partial class ConnectShellContent : UserControl
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IApplicationLifetimeService _applicationLifetimeService;
    private readonly ILogger<ConnectShellContent> _logger;
    private readonly RuntimeStartupDiagnostics _startup;
    private readonly TaskCompletionSource _rendered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _closed;

    /// <summary>Initializes the page and binds it to its view model.</summary>
    /// <param name="viewModel">The Connect page view model.</param>
    /// <param name="applicationLifetimeService">The authority that decides how the phase ends.</param>
    /// <param name="logger">The page logger.</param>
    /// <param name="startup">The phase startup diagnostics, told when the page is ready.</param>
    /// <param name="shell">The shell window that shows this page.</param>
    public ConnectShellContent(
        MainWindowViewModel viewModel,
        IApplicationLifetimeService applicationLifetimeService,
        ILogger<ConnectShellContent> logger,
        RuntimeStartupDiagnostics startup,
        IShellHost shell)
    {
        _viewModel = viewModel;
        _applicationLifetimeService = applicationLifetimeService;
        _logger = logger;
        _startup = startup;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoadedAsync;
        shell.FirstContentRendered += (_, _) => _rendered.TrySetResult();
        shell.Window.Closed += (_, _) => { _closed = true; _rendered.TrySetCanceled(); };
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedAsync;
        _logger.LogInformation("Connect content loaded. Starting asynchronous initialization.");

        try
        {
            await _startup.ObserveInitializationAsync(_viewModel.InitializeAsync, WaitForRenderedAsync,
                () => !_closed && _applicationLifetimeService.RequestedExitCode is null);
            _logger.LogInformation("Connect content asynchronous initialization finished.");
        }
        catch (OperationCanceledException) when (_closed || _applicationLifetimeService.RequestedExitCode is not null)
        {
        }
        catch (Exception ex)
        {
            _startup.ReportFailure(ex, "ui_initialization");
            _applicationLifetimeService.TryExit(ConnectStartupFailurePolicy.MapFailure(ConnectStartupStage.UiStartup, _startup.IsUiReady));
        }
    }

    private async Task WaitForRenderedAsync()
    {
        await _rendered.Task;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }
}
