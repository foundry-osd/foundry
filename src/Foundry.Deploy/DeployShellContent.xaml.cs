// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Foundry.Core.Services.Runtime;
using Foundry.Deploy.ViewModels;
using Foundry.Deploy.Views;
using Foundry.Wpf.Motion;
using Foundry.Wpf.Shell;

namespace Foundry.Deploy;

/// <summary>
/// The Foundry Deploy page of the WinPE shell window: the menu row and the splash, wizard and deployment status pages. It runs
/// the page initialization once it is loaded. The phase decides what a close request of the shell does (see
/// <see cref="DeployPhase"/>); <see cref="Detach"/> releases the page when the phase ends.
/// </summary>
public partial class DeployShellContent : UserControl
{
    private readonly MainWindowViewModel _viewModel;
    private readonly RuntimeStartupDiagnostics _startup;
    private readonly DeployPhaseLifetime _lifetime;
    private readonly IShellHost _shell;
    private readonly ShellContentAttachment _shellAttachment;
    private bool _closed;
    private DeploymentPage _previousPage = DeploymentPage.Splash;

    /// <remarks>
    /// The phase applies the theme and language chosen in Foundry Connect to <paramref name="viewModel"/> before it resolves
    /// this page, so the first frame already uses them.
    /// </remarks>
    internal DeployShellContent(MainWindowViewModel viewModel, RuntimeStartupDiagnostics startup, DeployPhaseLifetime lifetime, IShellHost shell)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _startup = startup;
        _lifetime = lifetime;
        _shell = shell;
        DataContext = viewModel;
        _viewModel.Session.PropertyChanged += OnSessionPropertyChanged;
        Loaded += OnLoaded;
        _shellAttachment = new ShellContentAttachment(shell, this, Detach);
    }

    /// <summary>
    /// Releases the page: stops the initialization observer, unsubscribes from the shell and disposes the view model. Called
    /// when the phase ends or the shell closes; safe to call more than once.
    /// </summary>
    internal void Detach()
    {
        _closed = true;
        _shellAttachment.Dispose();
        _viewModel.Session.PropertyChanged -= OnSessionPropertyChanged;
        _viewModel.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        try
        {
            await _startup.ObserveInitializationAsync(_viewModel.InitializeAsync, WaitForRenderedAsync, () => !_closed);
        }
        // The phase may end while a startup dialog's Network page is shown: the startup then stops before the page is released.
        catch (OperationCanceledException) when (_closed || _lifetime.Completion.IsCompleted)
        {
        }
        catch (Exception exception)
        {
            _startup.ReportFailure(exception, "ui_initialization");
            _lifetime.TryEnd(DeployPhase.FailureExitCodeFor("ui_initialization"));
        }
    }

    private async Task WaitForRenderedAsync()
    {
        await _shellAttachment.Rendered;
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
            Owner = _shell.Window
        };
        _ = dialog.ShowDialog();
    }
}
