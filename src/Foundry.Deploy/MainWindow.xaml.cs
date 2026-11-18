// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Foundry.Wpf.Motion;
using Foundry.Core.Services.Runtime;
using Foundry.Deploy.ViewModels;
using Foundry.Deploy.Views;

namespace Foundry.Deploy;

/// <summary>
/// The Foundry Deploy phase window. Closing it ends the phase, except while a deployment runs: the close then requests the
/// deployment's cancellation instead.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly RuntimeStartupDiagnostics _startup;
    private readonly DeployPhaseLifetime _lifetime;
    private readonly TaskCompletionSource _rendered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _closed;
    private DeploymentPage _previousPage = DeploymentPage.Splash;

    /// <remarks>
    /// The phase applies the theme and language chosen in Foundry Connect to <paramref name="viewModel"/> before it resolves
    /// this window, so the first frame already uses them.
    /// </remarks>
    internal MainWindow(MainWindowViewModel viewModel, RuntimeStartupDiagnostics startup, DeployPhaseLifetime lifetime)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _startup = startup;
        _lifetime = lifetime;
        DataContext = viewModel;
        _viewModel.Session.PropertyChanged += OnSessionPropertyChanged;
        Loaded += OnLoaded;
        ContentRendered += (_, _) => _rendered.TrySetResult();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel.IsDeploymentRunning)
        {
            e.Cancel = true;
            if (_viewModel.CancelDeploymentCommand.CanExecute(null))
            {
                _viewModel.CancelDeploymentCommand.Execute(null);
            }
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _rendered.TrySetCanceled();
        _viewModel.Session.PropertyChanged -= OnSessionPropertyChanged;
        _viewModel.Dispose();

        base.OnClosed(e);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        try
        {
            await _startup.ObserveInitializationAsync(_viewModel.InitializeAsync, WaitForRenderedAsync, () => !_closed);
        }
        catch (OperationCanceledException) when (_closed)
        {
        }
        catch (Exception exception)
        {
            _startup.ReportFailure(exception, "ui_initialization");
            _lifetime.TryEnd(DeployPhase.FailureExitCode);
        }
    }

    private async Task WaitForRenderedAsync()
    {
        await _rendered.Task;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DeploymentSessionViewModel.CurrentPage))
        {
            return;
        }

        DeploymentPage currentPage = _viewModel.Session.CurrentPage;
        if (currentPage == DeploymentPage.Wizard)
        {
            TransitionAnimator.FadeAndTranslateY(MainContentHost, MainContentTransform, 12);
        }
        else if (IsStatusPage(currentPage) && !IsStatusPage(_previousPage))
        {
            TransitionAnimator.FadeAndTranslateY(DeploymentStatusHost, DeploymentStatusTransform, 12);
        }

        _previousPage = currentPage;
    }

    private static bool IsStatusPage(DeploymentPage page)
    {
        return page is DeploymentPage.Progress or DeploymentPage.Success or DeploymentPage.Error;
    }

    private void ProgressPage_TechnicalDetailsRequested(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var dialog = new DeploymentErrorDetailsDialog
        {
            DataContext = _viewModel.Session,
            Owner = this
        };
        _ = dialog.ShowDialog();
    }
}
