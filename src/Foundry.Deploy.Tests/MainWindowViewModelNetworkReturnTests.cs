// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Runtime;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.DomainJoin;
using Foundry.Deploy.Services.Security;
using Foundry.Deploy.Services.Startup;
using Foundry.Deploy.Services.System;
using Foundry.Deploy.ViewModels;
using Foundry.Wpf.Shell;

namespace Foundry.Deploy.Tests;

/// <summary>What Deploy runs again when the Foundry Connect page returns, and the Network choice of the startup dialogs.</summary>
[Collection(nameof(WpfApplicationHostCollection))]
public sealed class MainWindowViewModelNetworkReturnTests
{
    private static readonly NetworkCacheStartupFailure Unreachable = new(NetworkCacheStartupFailureKind.Unreachable, @"\\srv\cache");

    [Fact]
    public Task Return_WhenNothingFailed_RunsNothingAgain() => WpfApplicationTestHost.RunAsync(async () =>
    {
        var coordinator = new FakeCoordinator(Snapshot(latestReleaseCheck: new BootMediaLatestReleaseCheck("26.10.3.1", "26.10.3.1")));
        var reader = new ScriptedReleaseReader("26.10.3.1");
        var navigator = new CallbackNetworkPageNavigator();
        using MainWindowViewModel viewModel = await StartAsync(coordinator, navigator, reader: reader);

        Assert.True(await navigator.OpenAsync(CancellationToken.None));

        Assert.Equal(1, coordinator.NetworkCacheChecks);
        Assert.Equal(0, coordinator.NetworkCacheResolutions);
        Assert.Equal(0, coordinator.GroupTagRefreshes);
        Assert.Equal(1, reader.Reads);
        Assert.Equal(1, coordinator.Initializations);
        Assert.Empty(WpfApplicationTestHost.UnhandledExceptions);
    });

    [Fact]
    public Task Return_WhenTheGroupTagDiscoveryFailed_DiscoversAgainOnceAndKeepsTheSelectedTag() => WpfApplicationTestHost.RunAsync(async () =>
    {
        var coordinator = new FakeCoordinator(Snapshot(groupTagsFailed: true))
        {
            GroupTagRefresh = new AutopilotGroupTagRefresh(Document(knownGroupTags: ["Kiosk", "Lab"]), Failed: false)
        };
        var navigator = new CallbackNetworkPageNavigator();
        using MainWindowViewModel viewModel = await StartAsync(coordinator, navigator);
        viewModel.Preparation.SelectedHardwareHashGroupTag = viewModel.Preparation.HardwareHashGroupTagOptions.Single(option => option.GroupTag == "Kiosk");

        await navigator.OpenAsync(CancellationToken.None);
        await navigator.OpenAsync(CancellationToken.None);

        Assert.Equal(1, coordinator.GroupTagRefreshes);
        Assert.Contains(viewModel.Preparation.HardwareHashGroupTagOptions, option => option.GroupTag == "Lab");
        Assert.Equal("Kiosk", viewModel.Preparation.SelectedHardwareHashGroupTag?.GroupTag);
        // The startup snapshot is not applied again: it would show the splash page and select the default tag.
        Assert.Equal(DeploymentPage.Wizard, viewModel.Session.CurrentPage);
        Assert.Equal(1, coordinator.Initializations);
        Assert.Empty(WpfApplicationTestHost.UnhandledExceptions);
    });

    [Fact]
    public Task Return_WhenTheNetworkCacheIsResolvedAgain_AppliesItsContent() => WpfApplicationTestHost.RunAsync(async () =>
    {
        var coordinator = new FakeCoordinator(Snapshot()) { NetworkCacheNeedsResolution = true };
        coordinator.Resolutions.Enqueue(new NetworkCacheStartupResult(Document(customImages: true), null));
        var dialog = new FakeCacheDialog();
        var navigator = new CallbackNetworkPageNavigator();
        using MainWindowViewModel viewModel = await StartAsync(coordinator, navigator, dialog);
        Assert.False(viewModel.CustomImages.IsEnabled);

        await navigator.OpenAsync(CancellationToken.None);

        Assert.Equal(1, coordinator.NetworkCacheResolutions);
        Assert.True(viewModel.CustomImages.IsEnabled);
        Assert.Empty(dialog.Failures);
        Assert.Equal(DeploymentPage.Wizard, viewModel.Session.CurrentPage);
        Assert.Empty(WpfApplicationTestHost.UnhandledExceptions);
    });

    [Fact]
    public Task Return_WhenTheNetworkCacheFails_ShowsTheStartupDialogAndContinueWithoutDropsTheContent() => WpfApplicationTestHost.RunAsync(async () =>
    {
        var coordinator = new FakeCoordinator(Snapshot(customImages: true)) { NetworkCacheNeedsResolution = true };
        coordinator.Resolutions.Enqueue(new NetworkCacheStartupResult(Document(customImages: true), Unreachable));
        var dialog = new FakeCacheDialog(NetworkCacheStartupChoice.ContinueWithout);
        var navigator = new CallbackNetworkPageNavigator();
        using MainWindowViewModel viewModel = await StartAsync(coordinator, navigator, dialog);
        Assert.True(viewModel.CustomImages.IsEnabled);

        await navigator.OpenAsync(CancellationToken.None);

        Assert.Equal([Unreachable], dialog.Failures);
        Assert.Equal([true], dialog.OfferedNetwork);
        Assert.False(viewModel.CustomImages.IsEnabled);
        Assert.Equal(1, coordinator.Initializations);
        Assert.Empty(WpfApplicationTestHost.UnhandledExceptions);
    });

    [Fact]
    public Task Return_CloseInTheNetworkCacheDialog_EndsDeployAndRunsNothingElse() => WpfApplicationTestHost.RunAsync(async () =>
    {
        var coordinator = new FakeCoordinator(Snapshot(groupTagsFailed: true)) { NetworkCacheNeedsResolution = true };
        coordinator.Resolutions.Enqueue(new NetworkCacheStartupResult(Document(), Unreachable));
        var shell = new RecordingShell();
        var navigator = new CallbackNetworkPageNavigator();
        using MainWindowViewModel viewModel = await StartAsync(coordinator, navigator, new FakeCacheDialog(NetworkCacheStartupChoice.Close), shell);

        await navigator.OpenAsync(CancellationToken.None);

        Assert.Equal(1, shell.ShutdownCount);
        Assert.Equal(0, coordinator.GroupTagRefreshes);
        Assert.Empty(WpfApplicationTestHost.UnhandledExceptions);
    });

    [Fact]
    public Task Return_NetworkInTheCacheDialog_ShowsThePageThenResolvesAgainWithoutANestedRefresh() => WpfApplicationTestHost.RunAsync(async () =>
    {
        var coordinator = new FakeCoordinator(Snapshot()) { NetworkCacheNeedsResolution = true };
        coordinator.Resolutions.Enqueue(new NetworkCacheStartupResult(Document(), Unreachable));
        coordinator.Resolutions.Enqueue(new NetworkCacheStartupResult(Document(customImages: true), null));
        var dialog = new FakeCacheDialog(NetworkCacheStartupChoice.Network);
        var navigator = new CallbackNetworkPageNavigator();
        using MainWindowViewModel viewModel = await StartAsync(coordinator, navigator, dialog);

        await navigator.OpenAsync(CancellationToken.None);

        Assert.Equal(2, navigator.Opens);
        Assert.Single(dialog.Failures);
        Assert.Equal(1, coordinator.NetworkCacheChecks);
        Assert.Equal(2, coordinator.NetworkCacheResolutions);
        Assert.True(viewModel.CustomImages.IsEnabled);
        Assert.Empty(WpfApplicationTestHost.UnhandledExceptions);
    });

    [Fact]
    public Task Startup_NetworkInTheCacheDialog_ShowsThePageThenRunsTheStartupAgain() => WpfApplicationTestHost.RunAsync(async () =>
    {
        var order = new List<string>();
        var coordinator = new FakeCoordinator(Snapshot(networkCacheFailure: Unreachable), Snapshot()) { OnInitialize = () => order.Add("startup") };
        var dialog = new FakeCacheDialog(NetworkCacheStartupChoice.Network);
        var navigator = new CallbackNetworkPageNavigator { OnPage = () => order.Add("page") };

        using MainWindowViewModel viewModel = await StartAsync(coordinator, navigator, dialog);

        Assert.Equal(["startup", "page", "startup"], order);
        Assert.Equal([true], dialog.OfferedNetwork);
        // The page opened from the dialog does not run the return refresh: the dialog's retry follows.
        Assert.Equal(0, coordinator.NetworkCacheChecks);
        Assert.True(viewModel.Session.IsStartupReady);
        Assert.Empty(WpfApplicationTestHost.UnhandledExceptions);
    });

    [Fact]
    public Task Return_RunsTheReleaseCheckAgainUntilItHasAResult() => WpfApplicationTestHost.RunAsync(async () =>
    {
        var coordinator = new FakeCoordinator(Snapshot(latestReleaseCheck: new BootMediaLatestReleaseCheck("26.10.3.1", "26.10.3.1")));
        var reader = new ScriptedReleaseReader(null, "26.11.2.1");
        var navigator = new CallbackNetworkPageNavigator();
        using MainWindowViewModel viewModel = await StartAsync(coordinator, navigator, reader: reader);
        Assert.False(viewModel.IsBootMediaUpdateRecommended);

        await navigator.OpenAsync(CancellationToken.None);
        await navigator.OpenAsync(CancellationToken.None);

        Assert.Equal(2, reader.Reads);
        Assert.True(viewModel.IsBootMediaUpdateRecommended);
        Assert.Empty(WpfApplicationTestHost.UnhandledExceptions);
    });

    [Fact]
    public Task Return_NetworkInTheCacheDialog_WhenThePhaseEnds_StopsWithoutResolvingAgain() => WpfApplicationTestHost.RunAsync(async () =>
    {
        var coordinator = new FakeCoordinator(Snapshot()) { NetworkCacheNeedsResolution = true };
        coordinator.Resolutions.Enqueue(new NetworkCacheStartupResult(Document(), Unreachable));
        var dialog = new FakeCacheDialog(NetworkCacheStartupChoice.Network);
        var navigator = new CallbackNetworkPageNavigator { EndPhaseAtOpen = 2 };
        using MainWindowViewModel viewModel = await StartAsync(coordinator, navigator, dialog);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => navigator.OpenAsync(CancellationToken.None));

        Assert.Equal(1, coordinator.NetworkCacheResolutions);
        Assert.Single(dialog.Failures);
        Assert.Empty(WpfApplicationTestHost.UnhandledExceptions);
    });

    [Fact]
    public Task Startup_NetworkInTheCacheDialog_WhenThePhaseEnds_StopsWithoutRunningTheStartupAgain() => WpfApplicationTestHost.RunAsync(async () =>
    {
        var coordinator = new FakeCoordinator(Snapshot(networkCacheFailure: Unreachable), Snapshot());
        var dialog = new FakeCacheDialog(NetworkCacheStartupChoice.Network);
        var navigator = new CallbackNetworkPageNavigator { EndPhaseAtOpen = 1 };
        using MainWindowViewModel viewModel = Create(coordinator, navigator, dialog);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(viewModel.InitializeAsync);

        Assert.Equal(1, coordinator.Initializations);
        Assert.Single(dialog.Failures);
        Assert.False(viewModel.Session.IsStartupReady);
        Assert.Empty(WpfApplicationTestHost.UnhandledExceptions);
    });

    [Fact]
    public Task Return_WhileTheRefreshRuns_KeepsStartAndNetworkDisabled() => WpfApplicationTestHost.RunAsync(async () =>
    {
        var refresh = new TaskCompletionSource<AutopilotGroupTagRefresh>(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new FakeCoordinator(Snapshot(groupTagsFailed: true)) { PendingGroupTagRefresh = refresh.Task };
        var navigator = new CallbackNetworkPageNavigator();
        using MainWindowViewModel viewModel = await StartAsync(coordinator, navigator);
        Assert.True(viewModel.StartDeploymentCommand.CanExecute(null));

        Task<bool> open = navigator.OpenAsync(CancellationToken.None);

        Assert.False(viewModel.StartDeploymentCommand.CanExecute(null));
        Assert.False(viewModel.OpenNetworkCommand.CanExecute(null));
        refresh.SetResult(new AutopilotGroupTagRefresh(Document(), Failed: false));
        Assert.True(await open);
        Assert.True(viewModel.StartDeploymentCommand.CanExecute(null));
        Assert.True(viewModel.OpenNetworkCommand.CanExecute(null));
        Assert.Empty(WpfApplicationTestHost.UnhandledExceptions);
    });

    private static async Task<MainWindowViewModel> StartAsync(FakeCoordinator coordinator, CallbackNetworkPageNavigator navigator,
        FakeCacheDialog? dialog = null, RecordingShell? shell = null, ScriptedReleaseReader? reader = null)
    {
        MainWindowViewModel viewModel = Create(coordinator, navigator, dialog, shell, reader);
        await viewModel.InitializeAsync();
        viewModel.Session.ShowWizard();
        return viewModel;
    }

    private static MainWindowViewModel Create(FakeCoordinator coordinator, CallbackNetworkPageNavigator navigator,
        FakeCacheDialog? dialog = null, RecordingShell? shell = null, ScriptedReleaseReader? reader = null)
    {
        MainWindowViewModel viewModel = ProgressPageContractTests.CreateMainWindowViewModel(new UnusedExecution(), accessGate: new OpenGate(),
            coordinator: coordinator, shell: shell ?? new RecordingShell(), networkPageNavigator: navigator,
            networkCacheDialog: dialog ?? new FakeCacheDialog(), readLatestReleaseVersion: (reader ?? new ScriptedReleaseReader("26.10.3.1")).ReadAsync);
        navigator.Refresh = viewModel.RefreshAfterNetworkReturnAsync;
        return viewModel;
    }

    private static DeploymentStartupSnapshot Snapshot(bool groupTagsFailed = false, NetworkCacheStartupFailure? networkCacheFailure = null,
        BootMediaLatestReleaseCheck? latestReleaseCheck = null, bool customImages = false) => new()
        {
            CacheRootPath = @"X:\Foundry\Runtime",
            DeployConfigurationDocument = Document(customImages: customImages),
            AutopilotGroupTagsFailed = groupTagsFailed,
            NetworkCacheFailure = networkCacheFailure,
            LatestReleaseCheck = latestReleaseCheck,
            AutopilotProfiles = [],
            MachineNamePreparation = new MachineNamePreparationResult(),
            DetectedHardware = null,
            HardwareDetectionFailureMessage = null,
            TargetDisks = [],
            CatalogSnapshot = new DeploymentCatalogSnapshot([], [])
        };

    // Hardware hash upload offers the group tags; a custom image section without a media binding is enabled without media access.
    private static FoundryDeployConfigurationDocument Document(IReadOnlyList<string>? knownGroupTags = null, bool customImages = false) => new()
    {
        Autopilot = new DeployAutopilotSettings
        {
            IsEnabled = true,
            ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
            HardwareHashUpload = new DeployAutopilotHardwareHashUploadSettings
            {
                DefaultGroupTag = "Sales",
                KnownGroupTags = knownGroupTags ?? ["Kiosk"]
            }
        },
        CustomImages = new() { IsEnabled = customImages }
    };

    /// <summary>Starts with the scripted snapshots (the last one repeats) and records what the return refresh asks for.</summary>
    private sealed class FakeCoordinator(params DeploymentStartupSnapshot[] snapshots) : IDeploymentStartupCoordinator
    {
        private readonly Queue<DeploymentStartupSnapshot> _snapshots = new(snapshots);

        public int Initializations { get; private set; }
        public int GroupTagRefreshes { get; private set; }
        public int NetworkCacheChecks { get; private set; }
        public int NetworkCacheResolutions { get; private set; }
        public bool NetworkCacheNeedsResolution { get; set; }
        public AutopilotGroupTagRefresh? GroupTagRefresh { get; set; }
        public Task<AutopilotGroupTagRefresh>? PendingGroupTagRefresh { get; set; }
        public Queue<NetworkCacheStartupResult?> Resolutions { get; } = new();
        public Action? OnInitialize { get; set; }

        public Task<DeploymentStartupSnapshot> InitializeAsync(DeploymentStartupRequest request, CancellationToken cancellationToken = default)
        {
            Initializations++;
            OnInitialize?.Invoke();
            return Task.FromResult(_snapshots.Count > 1 ? _snapshots.Dequeue() : _snapshots.Peek());
        }

        public Task<DeploymentCatalogSnapshot> ReloadCatalogsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AutopilotGroupTagRefresh> RefreshAutopilotGroupTagsAsync(CancellationToken cancellationToken = default)
        {
            GroupTagRefreshes++;
            return PendingGroupTagRefresh
                ?? Task.FromResult(GroupTagRefresh ?? throw new InvalidOperationException("No group tag refresh is scripted."));
        }

        public bool ShouldResolveNetworkCacheAgain()
        {
            NetworkCacheChecks++;
            return NetworkCacheNeedsResolution;
        }

        public Task<NetworkCacheStartupResult?> ResolveNetworkCacheAsync(CancellationToken cancellationToken = default)
        {
            NetworkCacheResolutions++;
            NetworkCacheNeedsResolution = false;
            return Task.FromResult(Resolutions.Dequeue());
        }
    }

    private sealed class FakeCacheDialog(params NetworkCacheStartupChoice[] choices) : INetworkCacheStartupDialogService
    {
        private readonly Queue<NetworkCacheStartupChoice> _choices = new(choices);
        public List<NetworkCacheStartupFailure> Failures { get; } = [];
        public List<bool> OfferedNetwork { get; } = [];

        public NetworkCacheStartupChoice ShowFailure(NetworkCacheStartupFailure failure, bool canOpenNetwork)
        {
            Failures.Add(failure);
            OfferedNetwork.Add(canOpenNetwork);
            return _choices.Dequeue();
        }
    }

    /// <summary>Returns the scripted versions in order (null is an unknown version); the last one repeats.</summary>
    private sealed class ScriptedReleaseReader(params string?[] versions)
    {
        private readonly Queue<string?> _versions = new(versions);
        public int Reads { get; private set; }

        public Task<string?> ReadAsync(CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(_versions.Count > 1 ? _versions.Dequeue() : _versions.Peek());
        }
    }

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

    private sealed class UnusedExecution : IDeploymentExecutionService
    {
        public Task<DeploymentExecutionRunResult> ExecuteAsync(DeploymentContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DeploymentExecutionRunResult> ExecuteAsync(DeploymentContext context, DomainJoinPreparedInput? domainJoinInput,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
