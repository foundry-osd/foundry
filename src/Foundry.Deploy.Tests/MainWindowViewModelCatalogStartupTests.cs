// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net.Http;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.DomainJoin;
using Foundry.Deploy.Services.Runtime;
using Foundry.Deploy.Services.Security;
using Foundry.Deploy.Services.Startup;
using Foundry.Deploy.Services.System;
using Foundry.Deploy.ViewModels;
using Foundry.Wpf.Shell;

namespace Foundry.Deploy.Tests;

/// <summary>The startup flow of the Deploy view model when the OS and driver catalogs cannot be loaded.</summary>
[Collection(nameof(WpfApplicationHostCollection))]
public sealed class MainWindowViewModelCatalogStartupTests
{
    [Fact]
    public Task WhenTheCatalogsFail_TheDialogOffersRetryAndRetryOpensTheWizard() => WpfApplicationTestHost.RunAsync(async () =>
    {
        var failure = new HttpRequestException("Catalog unavailable");
        var coordinator = new FakeCoordinator(failure, reloads: [null]);
        var dialog = new FakeDialog(CatalogStartupChoice.Retry);
        using MainWindowViewModel viewModel = Create(coordinator, dialog);

        await viewModel.InitializeAsync();

        Assert.Same(failure, Assert.Single(dialog.Failures));
        Assert.Equal([false], dialog.OfferedNetwork);
        Assert.Equal(1, coordinator.Initializations);
        Assert.Equal(1, coordinator.ReloadCount);
        Assert.True(viewModel.Session.IsStartupReady);
        Assert.Empty(WpfApplicationTestHost.UnhandledExceptions);
    });

    [Fact]
    public Task RetryPressedSeveralTimes_ReloadsEachTimeAndOpensTheWizardOnSuccess() => WpfApplicationTestHost.RunAsync(async () =>
    {
        var coordinator = new FakeCoordinator(new HttpRequestException("first"),
            reloads: [new HttpRequestException("second"), new HttpRequestException("third"), null]);
        var dialog = new FakeDialog(CatalogStartupChoice.Retry, CatalogStartupChoice.Retry, CatalogStartupChoice.Retry);
        using MainWindowViewModel viewModel = Create(coordinator, dialog);

        await viewModel.InitializeAsync();

        Assert.Equal(3, dialog.Failures.Count);
        Assert.Equal(3, coordinator.ReloadCount);
        Assert.Equal(1, coordinator.Initializations);
        Assert.True(viewModel.Session.IsStartupReady);
        Assert.Empty(WpfApplicationTestHost.UnhandledExceptions);
    });

    [Fact]
    public Task NetworkInTheDialog_ShowsTheNetworkPageThenReloadsTheCatalogs() => WpfApplicationTestHost.RunAsync(async () =>
    {
        var order = new List<string>();
        var coordinator = new FakeCoordinator(new HttpRequestException("Catalog unavailable"), reloads: [null], onReload: () => order.Add("reload"));
        var dialog = new FakeDialog(CatalogStartupChoice.Network);
        var navigator = new CallbackNetworkPageNavigator { OnPage = () => order.Add("page") };
        using MainWindowViewModel viewModel = Create(coordinator, dialog, navigator: navigator);
        navigator.Refresh = viewModel.RefreshAfterNetworkReturnAsync;

        await viewModel.InitializeAsync();

        Assert.Equal([true], dialog.OfferedNetwork);
        Assert.Equal(["page", "reload"], order);
        Assert.Equal(1, coordinator.Initializations);
        Assert.True(viewModel.Session.IsStartupReady);
        Assert.Empty(WpfApplicationTestHost.UnhandledExceptions);
    });

    [Fact]
    public Task CloseAfterFailures_EndsStartupWithTheOriginalCatalogFailure() => WpfApplicationTestHost.RunAsync(async () =>
    {
        var coordinator = new FakeCoordinator(new HttpRequestException("first"), reloads: [new InvalidOperationException("second")]);
        var dialog = new FakeDialog(CatalogStartupChoice.Retry, CatalogStartupChoice.Close);
        var shell = new RecordingShell();
        using MainWindowViewModel viewModel = Create(coordinator, dialog, shell);

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.InitializeAsync());

        Assert.Equal("second", thrown.Message);
        Assert.Equal(2, dialog.Failures.Count);
        Assert.False(viewModel.Session.IsStartupReady);
        Assert.Equal(0, shell.ShutdownCount);
        Assert.Empty(WpfApplicationTestHost.UnhandledExceptions);
    });

    [Fact]
    public Task CloseOnTheFirstFailure_RethrowsThatFailure() => WpfApplicationTestHost.RunAsync(async () =>
    {
        var failure = new HttpRequestException("Catalog unavailable");
        var coordinator = new FakeCoordinator(failure, reloads: []);
        using MainWindowViewModel viewModel = Create(coordinator, new FakeDialog(CatalogStartupChoice.Close));

        HttpRequestException thrown = await Assert.ThrowsAsync<HttpRequestException>(() => viewModel.InitializeAsync());

        Assert.Same(failure, thrown);
        Assert.Equal(0, coordinator.ReloadCount);
        Assert.Empty(WpfApplicationTestHost.UnhandledExceptions);
    });

    [Fact]
    public Task CancellationDuringTheReload_EndsStartupAsCancellationWithoutAnotherDialog() => WpfApplicationTestHost.RunAsync(async () =>
    {
        var coordinator = new FakeCoordinator(new HttpRequestException("first"), reloads: [], blockReload: true);
        var dialog = new FakeDialog(CatalogStartupChoice.Retry);
        using MainWindowViewModel viewModel = Create(coordinator, dialog);

        Task startup = viewModel.InitializeAsync();
        await coordinator.ReloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        viewModel.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startup);
        Assert.Single(dialog.Failures);
        Assert.Empty(WpfApplicationTestHost.UnhandledExceptions);
    });

    private static MainWindowViewModel Create(FakeCoordinator coordinator, FakeDialog dialog, RecordingShell? shell = null,
        CallbackNetworkPageNavigator? navigator = null) =>
        ProgressPageContractTests.CreateMainWindowViewModel(new UnusedExecution(), accessGate: new OpenGate(), coordinator: coordinator,
            shell: shell ?? new RecordingShell(), catalogDialog: dialog, networkPageNavigator: navigator);

    private sealed class OpenGate : IDeploymentAccessGate
    {
        public Task<bool> AuthorizeAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class RecordingShell : IApplicationShellService
    {
        public int ShutdownCount { get; private set; }
        public void ShowAbout() { }
        public bool ConfirmWarning(string title, string message) => true;
        public void Shutdown() => ShutdownCount++;
    }

    private sealed class FakeDialog(params CatalogStartupChoice[] choices) : ICatalogStartupDialogService
    {
        private readonly Queue<CatalogStartupChoice> _choices = new(choices);
        public List<Exception> Failures { get; } = [];
        public List<bool> OfferedNetwork { get; } = [];

        public CatalogStartupChoice ShowFailure(Exception failure, bool canOpenNetwork)
        {
            Failures.Add(failure);
            OfferedNetwork.Add(canOpenNetwork);
            return _choices.Dequeue();
        }
    }

    /// <summary>Starts with a catalog failure; each reload takes the next scripted outcome (null is a success).</summary>
    private sealed class FakeCoordinator(Exception initialFailure, Exception?[] reloads, bool blockReload = false, Action? onReload = null)
        : IDeploymentStartupCoordinator
    {
        private readonly Queue<Exception?> _reloads = new(reloads);
        public int Initializations { get; private set; }
        public int ReloadCount { get; private set; }
        public TaskCompletionSource ReloadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<DeploymentStartupSnapshot> InitializeAsync(DeploymentStartupRequest request, CancellationToken cancellationToken = default)
        {
            Initializations++;
            return Task.FromResult(new DeploymentStartupSnapshot
            {
                CacheRootPath = @"X:\Foundry\Runtime",
                DeployConfigurationDocument = null,
                AutopilotProfiles = [],
                MachineNamePreparation = new MachineNamePreparationResult(),
                DetectedHardware = null,
                HardwareDetectionFailureMessage = null,
                TargetDisks = [],
                CatalogSnapshot = new DeploymentCatalogSnapshot([], []),
                CatalogFailure = initialFailure
            });
        }

        public async Task<DeploymentCatalogSnapshot> ReloadCatalogsAsync(CancellationToken cancellationToken = default)
        {
            ReloadCount++;
            onReload?.Invoke();
            if (blockReload)
            {
                ReloadStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            Exception? outcome = _reloads.Dequeue();
            return outcome is null ? new DeploymentCatalogSnapshot([], []) : throw outcome;
        }

        public Task<AutopilotGroupTagRefresh> RefreshAutopilotGroupTagsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The return refresh must not run during startup.");

        public bool ShouldResolveNetworkCacheAgain() => throw new NotSupportedException("The return refresh must not run during startup.");

        public Task<NetworkCacheStartupResult?> ResolveNetworkCacheAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The return refresh must not run during startup.");
    }

    private sealed class UnusedExecution : IDeploymentExecutionService
    {
        public Task<DeploymentExecutionRunResult> ExecuteAsync(DeploymentContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DeploymentExecutionRunResult> ExecuteAsync(DeploymentContext context, DomainJoinPreparedInput? domainJoinInput,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
