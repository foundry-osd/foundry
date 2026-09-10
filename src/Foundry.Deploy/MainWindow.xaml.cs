// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Foundry.Deploy.Motion;
using Foundry.Deploy.Services.Runtime;
using Foundry.Deploy.ViewModels;

namespace Foundry.Deploy;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly RuntimeStartupDiagnostics _startup;
    private readonly TaskCompletionSource _rendered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _closed;
    private DeploymentPage _previousPage = DeploymentPage.Splash;

    public MainWindow(MainWindowViewModel viewModel, RuntimeStartupDiagnostics startup)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _startup = startup;
        DataContext = viewModel;
        _viewModel.Session.PropertyChanged += OnSessionPropertyChanged;
        Loaded += OnLoaded;
        ContentRendered += (_, _) => _rendered.TrySetResult();
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
            Application.Current?.Shutdown(1);
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

}
