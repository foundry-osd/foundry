// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Runtime.CompilerServices;
using Foundry.Connect.Network;
using Foundry.Connect.Security;
using Foundry.Connect.Tests.Support;
using Foundry.Connect.ViewModels;
using Foundry.Core.Models.Configuration;
using Foundry.Wpf.Localization;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Tests.ViewModels;

public sealed class ConnectPageViewModelTests : IDisposable
{
    private readonly ManualTimeProvider _clock = new();
    private readonly FakeConnectionCoordinator _coordinator = new();
    private readonly FakeUnlockService _unlock = new();
    private readonly FakeNetworkMonitor _monitor = new();
    private readonly FakeSuccessfulExitService _exit = new();
    private readonly ResourceLocalizationService _localization = TestLocalization.Create();
    private readonly List<ConnectFocusTarget> _focus = [];
    private readonly ConnectPageViewModel _page;

    public ConnectPageViewModelTests()
    {
        var actions = new ConnectNetworkActions(_coordinator, _unlock, new RecordingLogger<ConnectNetworkActions>());
        var dispatcher = new ImmediateUiDispatcher();
        var configuration = new FoundryConnectConfigurationDocument
        {
            Capabilities = new ConnectNetworkCapabilitiesSettings { WifiProvisioned = true },
            Wifi = new ConnectWifiSettings { IsEnabled = true, Ssid = "Foundry Lab", SecurityType = "WPA3-Personal" }
        };
        _page = new ConnectPageViewModel(
            _localization, _coordinator, _monitor, actions,
            new AutoContinueViewModel(_localization, _exit, new FakeApplicationLifetime(), _clock, dispatcher, new RecordingLogger<AutoContinueViewModel>(), isAutoContinueEnabled: true),
            new EthernetSectionViewModel(_localization),
            new ProvisionedWifiViewModel(_localization, actions, configuration),
            new WifiNetworksViewModel(_localization, actions, _monitor),
            new DiagnosticsViewModel(_localization, new RecordingLogger<DiagnosticsViewModel>()),
            dispatcher, _clock, new RecordingLogger<ConnectPageViewModel>());
        _page.FocusRequested += (_, target) => _focus.Add(target);
    }

    public void Dispose() => _page.Dispose();

    private string Text(string key) => _localization.GetString(key);

    private void Publish(ConnectDebugScenario scenario) => _coordinator.Publish(DebugScenarioCatalog.Create(scenario, _clock.GetUtcNow()));

    public sealed record PageExpectation(
        ConnectPageStatus Status, BlockedReason Reason, string TitleKey, string? DescriptionKey, bool Busy,
        bool EthernetCard, bool EthernetNotice, bool Provisioned, bool WifiNetworks, string? WifiNoticeKey,
        bool CaptivePortal, bool Dot1xRow, bool CanContinue, ConnectFocusTarget Focus);

    public static TheoryData<ConnectDebugScenario, PageExpectation> Matrix => new()
    {
        { ConnectDebugScenario.Checking, new(ConnectPageStatus.Checking, BlockedReason.None, "Status.CheckingTitle", null, true, false, false, false, false, null, false, false, false, ConnectFocusTarget.None) },
        { ConnectDebugScenario.NoAdapter, new(ConnectPageStatus.Blocked, BlockedReason.NoAdapter, "Status.BlockedTitle", "Status.BlockedNoEthernetAdapterDescription", false, false, true, false, false, null, false, false, false, ConnectFocusTarget.Retry) },
        { ConnectDebugScenario.NoLink, new(ConnectPageStatus.Blocked, BlockedReason.NoLink, "Status.BlockedTitle", "Status.BlockedNoCableDescription", false, true, false, false, false, null, false, false, false, ConnectFocusTarget.Retry) },
        { ConnectDebugScenario.WaitingForAddress, new(ConnectPageStatus.Blocked, BlockedReason.NoIpAddress, "Status.BlockedTitle", "Status.BlockedNoIpAddressDescription", false, true, false, false, false, null, false, false, false, ConnectFocusTarget.Retry) },
        { ConnectDebugScenario.ApipaAddress, new(ConnectPageStatus.Blocked, BlockedReason.NoIpAddress, "Status.BlockedTitle", "Status.BlockedNoIpAddressDescription", false, true, false, false, false, null, false, false, false, ConnectFocusTarget.Retry) },
        { ConnectDebugScenario.EthernetNoInternet, new(ConnectPageStatus.Blocked, BlockedReason.NoInternet, "Status.BlockedTitle", "Status.BlockedNoInternetDescription", false, true, false, false, false, null, false, false, false, ConnectFocusTarget.Retry) },
        { ConnectDebugScenario.CaptivePortal, new(ConnectPageStatus.Blocked, BlockedReason.CaptivePortal, "Status.BlockedTitle", "Status.BlockedCaptivePortalDescription", false, true, false, false, false, null, true, false, false, ConnectFocusTarget.Retry) },
        { ConnectDebugScenario.EthernetReady, new(ConnectPageStatus.Ready, BlockedReason.None, "Status.NetworkReadyTitle", null, false, true, false, false, false, null, false, false, true, ConnectFocusTarget.Continue) },
        { ConnectDebugScenario.Dot1xFailed, new(ConnectPageStatus.Blocked, BlockedReason.WiredAuthentication, "Status.BlockedTitle", "Status.BlockedDot1xDescription", false, true, false, false, false, null, false, true, false, ConnectFocusTarget.Retry) },
        { ConnectDebugScenario.WifiRuntimeMissing, new(ConnectPageStatus.Blocked, BlockedReason.NoLink, "Status.BlockedTitle", "Status.BlockedNoCableDescription", false, true, false, false, false, "Wifi.UnavailableAtRuntime", false, false, false, ConnectFocusTarget.Retry) },
        { ConnectDebugScenario.NoWifiAdapter, new(ConnectPageStatus.Blocked, BlockedReason.NoLink, "Status.BlockedTitle", "Status.BlockedNoCableDescription", false, true, false, false, false, "Wifi.NoAdapterDetected", false, false, false, ConnectFocusTarget.Retry) },
        { ConnectDebugScenario.WifiNetworks, new(ConnectPageStatus.Blocked, BlockedReason.NoLink, "Status.BlockedTitle", "Status.BlockedNoLinkDescription", false, true, false, false, true, null, false, false, false, ConnectFocusTarget.WifiNetworks) },
        { ConnectDebugScenario.ProvisionedWifiUnavailable, new(ConnectPageStatus.Blocked, BlockedReason.NoLink, "Status.BlockedTitle", "Status.BlockedNoLinkDescription", false, true, false, true, true, null, false, false, false, ConnectFocusTarget.WifiNetworks) },
        { ConnectDebugScenario.WifiReady, new(ConnectPageStatus.Ready, BlockedReason.None, "Status.NetworkReadyTitle", null, false, true, false, true, true, null, false, false, true, ConnectFocusTarget.Continue) }
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Scenario_RendersTheExpectedPage(ConnectDebugScenario scenario, PageExpectation expected)
    {
        Publish(scenario);

        Assert.Equal(expected.Status, _page.Status);
        Assert.Equal(expected.Reason, _page.BlockedReason);
        Assert.Equal(Text(expected.TitleKey), _page.HeroTitle);
        Assert.Equal(expected.DescriptionKey is null ? string.Empty : Text(expected.DescriptionKey), _page.HeroDescription);
        Assert.Equal(expected.DescriptionKey is not null, _page.HasHeroDescription);
        Assert.Equal(expected.Busy, _page.IsHeroBusy);
        Assert.Equal(!expected.Busy, _page.IsHeroTerminal);
        Assert.Equal(expected.Status == ConnectPageStatus.Blocked, _page.IsBlocked);
        Assert.Equal(expected.EthernetCard, _page.Ethernet.ShowCard);
        Assert.Equal(expected.EthernetNotice, _page.Ethernet.ShowNoAdapterNotice);
        Assert.Equal(expected.Provisioned, _page.ProvisionedWifi.IsVisible);
        Assert.Equal(expected.WifiNetworks, _page.WifiNetworks.IsVisible);
        Assert.Equal(expected.WifiNoticeKey is null ? string.Empty : Text(expected.WifiNoticeKey), _page.WifiNoticeText);
        Assert.Equal(expected.CaptivePortal, _page.ShowCaptivePortalNotice);
        Assert.Equal(expected.Dot1xRow, _page.Ethernet.HasDot1x);

        // The only 802.1X row in the matrix is a failure, so the row and its InfoBar go together.
        Assert.Equal(expected.Dot1xRow, _page.Ethernet.ShowDot1xNotice);
        Assert.Equal(expected.CanContinue, _page.AutoContinue.ContinueCommand.CanExecute(null));
        Assert.Equal(expected.Focus == ConnectFocusTarget.None ? [] : [expected.Focus], _focus);
    }

    [Fact]
    public void WifiReady_ShowsTheProvisionedChipAndCountsDown()
    {
        Publish(ConnectDebugScenario.WifiReady);

        Assert.Equal(_localization.Format("Wifi.ConnectionChipProvisionedFormat", "Foundry Lab"), _page.ConnectionChipText);
        Assert.True(_page.AutoContinue.IsCounting);
    }

    [Theory]
    [InlineData(NetworkOperationKind.ApplyProvisioning, "Status.ApplyingProvisioningDescription")]
    [InlineData(NetworkOperationKind.ConnectWifiNetwork, "Status.ConnectingDescription")]
    public void RunningOperation_ShowsWaiting(NetworkOperationKind operation, string descriptionKey)
    {
        _coordinator.Publish(DebugScenarioCatalog.Create(ConnectDebugScenario.WifiNetworks, _clock.GetUtcNow()) with { ActiveOperation = operation });

        Assert.Equal(ConnectPageStatus.Waiting, _page.Status);
        Assert.True(_page.IsHeroBusy);
        Assert.Equal(Text("Status.ConnectingTitle"), _page.HeroTitle);
        Assert.Equal(Text(descriptionKey), _page.HeroDescription);
    }

    [Fact]
    public void FocusRequested_OnlyOnStatusTransitions()
    {
        Publish(ConnectDebugScenario.EthernetNoInternet);
        _coordinator.Publish(DebugScenarioCatalog.Create(ConnectDebugScenario.EthernetNoInternet, _clock.GetUtcNow().AddSeconds(10)));
        Publish(ConnectDebugScenario.EthernetReady);

        Assert.Equal([ConnectFocusTarget.Retry, ConnectFocusTarget.Continue], _focus);
    }

    [Fact]
    public void Blocked_WithAConnectableProvisionedProfile_FocusesItsConnectButton()
    {
        NetworkState state = DebugScenarioCatalog.Create(ConnectDebugScenario.WifiNetworks, _clock.GetUtcNow());

        _coordinator.Publish(state with
        {
            Wifi = state.Wifi with { Provisioned = new ProvisionedWifiState(ProvisionedWifiStatus.Ready, Ssid: WifiSsid.FromText("Foundry Lab")) }
        });

        Assert.Equal(ConnectPageStatus.Blocked, _page.Status);
        Assert.Equal([ConnectFocusTarget.ProvisionedConnect], _focus);
    }

    [Fact]
    public void OnlineLost_DuringTheCountdown_StopsItAndFocusesRetry()
    {
        Publish(ConnectDebugScenario.EthernetReady);
        Assert.True(_page.AutoContinue.IsCounting);

        Publish(ConnectDebugScenario.EthernetNoInternet);

        Assert.False(_page.AutoContinue.IsCounting);
        Assert.False(_page.AutoContinue.ContinueCommand.CanExecute(null));
        Assert.Equal([ConnectFocusTarget.Continue, ConnectFocusTarget.Retry], _focus);
    }

    [Fact]
    public void NetworkingInteraction_PausesTheCountdownAndKeepsFocus()
    {
        Publish(ConnectDebugScenario.WifiReady);
        _focus.Clear();

        _page.WifiNetworks.SelectedNetwork = _page.WifiNetworks.Networks[1];
        Publish(ConnectDebugScenario.WifiNetworks);

        Assert.True(_page.AutoContinue.IsPaused);
        Assert.Empty(_focus);
    }

    [Fact]
    public void Ready_AfterANetworkingInteraction_KeepsFocusAndOffersContinueWithoutCounting()
    {
        Publish(ConnectDebugScenario.WifiNetworks);
        _focus.Clear();
        _page.WifiNetworks.SelectedNetwork = _page.WifiNetworks.Networks[0];

        Publish(ConnectDebugScenario.WifiReady);

        Assert.Equal(ConnectPageStatus.Ready, _page.Status);
        Assert.Empty(_focus);
        Assert.True(_page.AutoContinue.IsPaused);
        Assert.False(_page.AutoContinue.IsContinueDefault);
        Assert.True(_page.AutoContinue.ContinueCommand.CanExecute(null));
    }

    [Fact]
    public void StayHere_MovesTheFocusToContinue()
    {
        Publish(ConnectDebugScenario.EthernetReady);
        _focus.Clear();

        _page.AutoContinue.StayHereCommand.Execute(null);

        Assert.Equal([ConnectFocusTarget.Continue], _focus);
    }

    [Fact]
    public async Task Simulation_IgnoresLiveStateAndNeverActs()
    {
        _page.ApplyDebugScenario(ConnectDebugScenario.WifiNetworks);
        _coordinator.Publish(DebugScenarioCatalog.Create(ConnectDebugScenario.EthernetReady, _clock.GetUtcNow()));
        Assert.True(_page.IsSimulation);
        Assert.Equal(BlockedReason.NoLink, _page.BlockedReason);

        _page.WifiNetworks.SelectedNetwork = _page.WifiNetworks.Networks.Single(item => item.DisplayName == "Workshop");
        _page.WifiNetworks.SetPassphrase("password1".AsSpan());
        await _page.WifiNetworks.ConnectCommand.ExecuteAsync(null);
        _page.ApplyDebugScenario(ConnectDebugScenario.EthernetReady);
        for (int second = 0; second < 10; second++)
        {
            _clock.Advance(AutoContinueViewModel.TickInterval);
        }

        await (_page.AutoContinue.PendingCompletion ?? Task.CompletedTask);
        Assert.Empty(_coordinator.ConnectNetworkCalls);
        Assert.Equal(0, _exit.Calls);

        _page.ApplyDebugScenario(ConnectDebugScenario.Live);
        Assert.False(_page.IsSimulation);
        Assert.Equal(ConnectPageStatus.Ready, _page.Status);
    }

    [Fact]
    public void LanguageChange_RerendersWithoutRefreshOrFocus()
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        Publish(ConnectDebugScenario.WifiNetworks);
        _focus.Clear();
        WifiNetworkItemViewModel row = _page.WifiNetworks.Networks[0];
        List<string?> raised = [];
        _page.PropertyChanged += (_, args) => raised.Add($"Page.{args.PropertyName}");
        row.PropertyChanged += (_, args) => raised.Add($"Row.{args.PropertyName}");
        _page.AutoContinue.PropertyChanged += (_, args) => raised.Add($"AutoContinue.{args.PropertyName}");

        try
        {
            _localization.SetCulture(CultureInfo.GetCultureInfo(original.Name == "fr-FR" ? "de-DE" : "fr-FR"));

            Assert.Equal(Text("Status.BlockedTitle"), _page.HeroTitle);
            Assert.Equal(Text("Status.BlockedNoLinkDescription"), _page.HeroDescription);
            Assert.Equal(0, _monitor.RefreshRequests);
            Assert.Empty(_focus);
            Assert.Same(row, _page.WifiNetworks.Networks[0]);
            Assert.Contains("Page.HeroTitle", raised);
            Assert.Contains("Page.HeroDescription", raised);
            Assert.Contains("Page.WifiNoticeText", raised);
            Assert.Contains("Page.ConnectionChipText", raised);
            Assert.Contains("Row.SecurityText", raised);
            Assert.Contains("Row.AutomationName", raised);
            Assert.Contains("AutoContinue.CaptionText", raised);
        }
        finally
        {
            _localization.SetCulture(original);
        }
    }

    [Theory]
    [InlineData(ConnectUnlockResult.Unlocked)]
    [InlineData(ConnectUnlockResult.Cancelled)]
    public async Task InitializeAsync_UnlocksWiredBeforeProvisioning(ConnectUnlockResult unlockResult)
    {
        _unlock.IsWiredUnlockRequired = true;
        _unlock.Result = unlockResult;
        int provisioningCallsAtUnlock = -1;
        _unlock.OnUnlock = () => provisioningCallsAtUnlock = _coordinator.ApplyProvisioningCalls;

        await _page.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal([ConnectUnlockTrigger.WiredStartup], _unlock.Calls);
        Assert.Equal(0, provisioningCallsAtUnlock);
        Assert.Equal(1, _coordinator.ApplyProvisioningCalls);
        Assert.Equal(1, _monitor.RunCalls);
    }

    [Fact]
    public async Task InitializeAsync_AfterTheWiredPrompt_OffersContinueWithoutCountingOrFocus()
    {
        _unlock.IsWiredUnlockRequired = true;
        _unlock.Result = ConnectUnlockResult.Unlocked;
        await _page.InitializeAsync(TestContext.Current.CancellationToken);

        Publish(ConnectDebugScenario.EthernetReady);

        Assert.True(_page.AutoContinue.IsPaused);
        Assert.False(_page.AutoContinue.IsContinueDefault);
        Assert.True(_page.AutoContinue.ContinueCommand.CanExecute(null));
        Assert.Empty(_focus);
    }

    [Fact]
    public void UnlockRequested_PromptsForWifiUnlessSimulatedOrDisposed()
    {
        _unlock.IsWifiUnlockRequired = true;

        _coordinator.RaiseUnlockRequested();
        _page.ApplyDebugScenario(ConnectDebugScenario.WifiNetworks);
        _coordinator.RaiseUnlockRequested();
        _page.ApplyDebugScenario(ConnectDebugScenario.Live);
        _page.Dispose();
        _coordinator.RaiseUnlockRequested();

        Assert.Equal([ConnectUnlockTrigger.AutomaticWifi], _unlock.Calls);
    }

    [Fact]
    public void AutomaticUnlock_ThenOnline_OffersContinueWithoutCountingOrFocus()
    {
        _unlock.IsWifiUnlockRequired = true;
        _unlock.Result = ConnectUnlockResult.Unlocked;
        Publish(ConnectDebugScenario.EthernetNoInternet);
        _focus.Clear();

        _coordinator.RaiseUnlockRequested();
        Publish(ConnectDebugScenario.WifiReady);

        Assert.Equal(1, _coordinator.RefreshProvisionedWifiCalls);
        Assert.Equal(ConnectPageStatus.Ready, _page.Status);
        Assert.False(_page.AutoContinue.IsCounting);
        Assert.True(_page.AutoContinue.IsPaused);
        Assert.False(_page.AutoContinue.IsContinueDefault);
        Assert.True(_page.AutoContinue.ContinueCommand.CanExecute(null));
        Assert.Empty(_focus);
    }

    [Fact]
    public void EthernetOnline_WhileTheAutomaticPromptIsOpen_NeverExits()
    {
        _unlock.IsWifiUnlockRequired = true;
        _unlock.Result = ConnectUnlockResult.Cancelled;
        Publish(ConnectDebugScenario.EthernetNoInternet);
        bool countingWhilePrompting = true;
        _unlock.OnUnlock = () =>
        {
            Publish(ConnectDebugScenario.EthernetReady);
            for (int second = 0; second <= 10; second++)
            {
                _clock.Advance(AutoContinueViewModel.TickInterval);
            }

            countingWhilePrompting = _page.AutoContinue.IsCounting;
        };

        _coordinator.RaiseUnlockRequested();

        Assert.False(countingWhilePrompting);
        Assert.Null(_page.AutoContinue.PendingCompletion);
        Assert.Equal(0, _exit.Calls);
        Assert.Equal(ConnectPageStatus.Ready, _page.Status);
    }

    [Fact]
    public void UnlockRequested_WhenThePromptFails_LogsTheFailureTypeOnly()
    {
        var logger = new RecordingLogger<ConnectPageViewModel>();
        using ConnectPageViewModel page = CreatePage(logger);
        _unlock.IsWifiUnlockRequired = true;
        _unlock.OnUnlock = () => throw new InvalidOperationException("Foundry Lab profile failed.");

        _coordinator.RaiseUnlockRequested();

        RecordedLogEntry entry = Assert.Single(logger.Entries, static entry => entry.Level == LogLevel.Warning);
        Assert.Equal("Automatic deployment password prompt failed. FailureType=InvalidOperationException", entry.Message);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public async Task InitializeAsync_AppliesProvisioningOnceThenStartsTheMonitor()
    {
        _coordinator.ProvisioningResult = new ProvisioningOutcome(
            Dot1xState.NotConfigured,
            new ProvisionedWifiState(ProvisionedWifiStatus.Ready, Ssid: WifiSsid.FromText("Foundry Lab")),
            null,
            [NetworkFailureCode.WifiProfileRejected]);

        await _page.InitializeAsync(TestContext.Current.CancellationToken);
        await _page.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, _coordinator.ApplyProvisioningCalls);
        Assert.Equal(1, _monitor.RunCalls);
        Assert.Equal(Text("Wifi.ActionApplyProvisionedFailed"), _page.ProvisionedWifi.FeedbackText);
    }

    [Fact]
    public void RefreshCommand_RequestsAMonitorRefresh()
    {
        _page.RefreshCommand.Execute(null);

        Assert.Equal(1, _monitor.RefreshRequests);
    }

    [Fact]
    public async Task Dispose_DuringTheWiredPrompt_NeverProvisionsNorStartsTheMonitor()
    {
        _unlock.IsWiredUnlockRequired = true;
        _unlock.Result = ConnectUnlockResult.Unlocked;
        _unlock.OnUnlock = _page.Dispose;

        await _page.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal([ConnectUnlockTrigger.WiredStartup], _unlock.Calls);
        Assert.Equal(0, _coordinator.ApplyProvisioningCalls);
        Assert.Equal(0, _monitor.RunCalls);
    }

    [Fact]
    public async Task Dispose_DuringProvisioning_NeverStartsTheMonitor()
    {
        _coordinator.ProvisioningGate = new TaskCompletionSource<ProvisioningOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task initialize = _page.InitializeAsync(TestContext.Current.CancellationToken);

        _page.Dispose();
        _coordinator.ProvisioningGate.TrySetResult(ProvisioningOutcome.NotConfigured);
        await initialize;

        Assert.Equal(0, _monitor.RunCalls);
    }

    [Fact]
    public async Task Dispose_WhileProvisioningIgnoresCancellation_NeverStartsTheMonitor()
    {
        _coordinator.ProvisioningIgnoresCancellation = true;
        _coordinator.ProvisioningGate = new TaskCompletionSource<ProvisioningOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task initialize = _page.InitializeAsync(TestContext.Current.CancellationToken);

        _page.Dispose();
        _coordinator.ProvisioningGate.TrySetResult(new ProvisioningOutcome(
            Dot1xState.NotConfigured,
            new ProvisionedWifiState(ProvisionedWifiStatus.Ready, Ssid: WifiSsid.FromText("Foundry Lab")),
            null,
            [NetworkFailureCode.WifiProfileRejected]));
        await initialize;

        Assert.Equal(0, _monitor.RunCalls);
        Assert.False(_page.ProvisionedWifi.HasFeedback);
    }

    [Fact]
    public async Task Dispose_ObservesAFaultedMonitor()
    {
        var fault = new InvalidOperationException("Monitor loop failed.");
        bool unobserved = false;
        EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, args) => unobserved |= args.Exception.InnerExceptions.Contains(fault);
        TaskScheduler.UnobservedTaskException += handler;
        try
        {
            WeakReference monitorTask = await StartAndDisposeWithFaultedMonitorAsync(fault);

            // The faulted task can only be collected once nothing, the observing continuation included, references it.
            for (int attempt = 0; attempt < 100 && monitorTask.IsAlive; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            Assert.False(monitorTask.IsAlive);
            Assert.False(unobserved);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= handler;
        }
    }

    [Fact]
    public async Task Dispose_IsIdempotentAndStopsRendering()
    {
        await _page.InitializeAsync(TestContext.Current.CancellationToken);

        _page.Dispose();
        _page.Dispose();
        Publish(ConnectDebugScenario.EthernetReady);
        await _page.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConnectPageStatus.Checking, _page.Status);
        Assert.Equal(1, _monitor.RunCalls);
    }

    [Fact]
    public void StateChanged_RendersTheCurrentStateNotTheStaleArgument()
    {
        _coordinator.State = DebugScenarioCatalog.Create(ConnectDebugScenario.EthernetReady, _clock.GetUtcNow());

        _coordinator.RaiseStale(DebugScenarioCatalog.Create(ConnectDebugScenario.NoLink, _clock.GetUtcNow()));

        Assert.Equal(ConnectPageStatus.Ready, _page.Status);
    }

    [Fact]
    public void ApplyDebugScenario_LogsTheScenarioOnly()
    {
        var logger = new RecordingLogger<ConnectPageViewModel>();
        using ConnectPageViewModel page = CreatePage(logger);

        page.ApplyDebugScenario(ConnectDebugScenario.WifiReady);

        RecordedLogEntry entry = Assert.Single(logger.Entries);
        Assert.Equal("Debug scenario applied. Scenario=WifiReady", entry.Message);
    }

    // Not inlined: once it returns, nothing in the test references the page, the monitor or the faulted task.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<WeakReference> StartAndDisposeWithFaultedMonitorAsync(Exception fault)
    {
        var monitor = new FakeNetworkMonitor { RunResult = Task.FromException(fault) };
        ConnectPageViewModel page = CreatePage(new RecordingLogger<ConnectPageViewModel>(), monitor);
        await page.InitializeAsync(TestContext.Current.CancellationToken);
        var monitorTask = new WeakReference(monitor.RunResult);
        page.Dispose();
        return monitorTask;
    }

    private ConnectPageViewModel CreatePage(RecordingLogger<ConnectPageViewModel> logger, FakeNetworkMonitor? monitor = null)
    {
        var actions = new ConnectNetworkActions(_coordinator, _unlock, new RecordingLogger<ConnectNetworkActions>());
        var dispatcher = new ImmediateUiDispatcher();
        return new ConnectPageViewModel(
            _localization, _coordinator, monitor ?? _monitor, actions,
            new AutoContinueViewModel(_localization, _exit, new FakeApplicationLifetime(), _clock, dispatcher, new RecordingLogger<AutoContinueViewModel>(), isAutoContinueEnabled: false),
            new EthernetSectionViewModel(_localization),
            new ProvisionedWifiViewModel(_localization, actions, new FoundryConnectConfigurationDocument()),
            new WifiNetworksViewModel(_localization, actions, _monitor),
            new DiagnosticsViewModel(_localization, new RecordingLogger<DiagnosticsViewModel>()),
            dispatcher, _clock, logger);
    }
}
