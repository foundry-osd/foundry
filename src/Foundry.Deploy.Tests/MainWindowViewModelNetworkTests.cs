// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Network;
using Foundry.Deploy.ViewModels;

namespace Foundry.Deploy.Tests;

[Collection(nameof(WpfApplicationHostCollection))]
public sealed class MainWindowViewModelNetworkTests
{
    [Fact]
    public Task NetworkMenu_WithoutTheHostCallback_IsHiddenAndCannotOpen() => WpfApplicationTestHost.RunAsync(() =>
    {
        using MainWindowViewModel viewModel = Create(new FakeNavigator(isAvailable: false));
        viewModel.Session.CompleteStartupInitialization();
        viewModel.Session.ShowWizard();

        Assert.False(viewModel.IsNetworkMenuVisible);
        Assert.False(viewModel.OpenNetworkCommand.CanExecute(null));
        return Task.CompletedTask;
    });

    [Fact]
    public Task OpenNetwork_IsOfferedOnTheWizardOnly() => WpfApplicationTestHost.RunAsync(() =>
    {
        using MainWindowViewModel viewModel = Create(new FakeNavigator());
        Assert.True(viewModel.IsNetworkMenuVisible);
        Assert.False(viewModel.OpenNetworkCommand.CanExecute(null));

        viewModel.Session.CompleteStartupInitialization();
        Assert.Equal(DeploymentPage.Splash, viewModel.Session.CurrentPage);
        Assert.False(viewModel.OpenNetworkCommand.CanExecute(null));

        viewModel.Session.ShowWizard();
        Assert.True(viewModel.OpenNetworkCommand.CanExecute(null));

        viewModel.Session.BeginDeployment("PC", []);
        Assert.False(viewModel.OpenNetworkCommand.CanExecute(null));

        viewModel.Session.CompleteDeployment(null);
        Assert.Equal(DeploymentPage.Success, viewModel.Session.CurrentPage);
        Assert.False(viewModel.OpenNetworkCommand.CanExecute(null));

        viewModel.Session.FailDeployment("Step", "Failed");
        Assert.Equal(DeploymentPage.Error, viewModel.Session.CurrentPage);
        Assert.False(viewModel.OpenNetworkCommand.CanExecute(null));
        return Task.CompletedTask;
    });

    [Fact]
    public Task OpenNetwork_WhileADeploymentRuns_IsDisabledAndTheChangeIsNotified() => WpfApplicationTestHost.RunAsync(() =>
    {
        using MainWindowViewModel viewModel = Create(new FakeNavigator());
        viewModel.Session.CompleteStartupInitialization();
        viewModel.Session.ShowWizard();
        int notifications = 0;
        viewModel.OpenNetworkCommand.CanExecuteChanged += (_, _) => notifications++;

        viewModel.IsDeploymentRunning = true;

        Assert.False(viewModel.OpenNetworkCommand.CanExecute(null));
        Assert.True(notifications > 0);
        return Task.CompletedTask;
    });

    [Fact]
    public Task OpenNetwork_OpensThePageOnceAndIsDisabledUntilItReturns() => WpfApplicationTestHost.RunAsync(async () =>
    {
        var navigator = new FakeNavigator();
        using MainWindowViewModel viewModel = Create(navigator);
        viewModel.Session.CompleteStartupInitialization();
        viewModel.Session.ShowWizard();

        Task open = viewModel.OpenNetworkCommand.ExecuteAsync(null);

        Assert.Equal(1, navigator.Opens);
        Assert.False(viewModel.OpenNetworkCommand.CanExecute(null));
        navigator.Return(true);
        await open;
        Assert.True(viewModel.OpenNetworkCommand.CanExecute(null));
    });

    private static MainWindowViewModel Create(INetworkPageNavigator navigator) =>
        ProgressPageContractTests.CreateMainWindowViewModel(new UnusedExecution(), networkPageNavigator: navigator);

    private sealed class FakeNavigator(bool isAvailable = true) : INetworkPageNavigator
    {
        private TaskCompletionSource<bool>? _pending;

        public bool IsAvailable => isAvailable;

        public bool IsOpen => _pending is not null;

        public int Opens { get; private set; }

        public Task<bool> OpenAsync(CancellationToken cancellationToken)
        {
            Opens++;
            _pending = new TaskCompletionSource<bool>();
            return _pending.Task;
        }

        public void Return(bool opened)
        {
            TaskCompletionSource<bool> pending = _pending!;
            _pending = null;
            pending.SetResult(opened);
        }

        public Task CancelAndWaitAsync() => Task.CompletedTask;
    }

    private sealed class UnusedExecution : IDeploymentExecutionService
    {
        public Task<DeploymentExecutionRunResult> ExecuteAsync(DeploymentContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DeploymentExecutionRunResult> ExecuteAsync(DeploymentContext context,
            Foundry.Deploy.Services.DomainJoin.DomainJoinPreparedInput? domainJoinInput, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
