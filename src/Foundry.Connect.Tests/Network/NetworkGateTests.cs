// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.CompilerServices;
using Foundry.Connect.Configuration;
using Foundry.Connect.Lifetime;
using Foundry.Connect.Network;
using Foundry.Connect.Network.Policy;
using Foundry.Connect.Security;
using Foundry.Connect.Tests.Support;
using Foundry.Connect.ViewModels;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Runtime;
using Foundry.Wpf.Localization;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Tests.Network;

[Collection(nameof(ProcessCultureCollection))]
public sealed class NetworkGateTests : IDisposable
{
    private readonly ManualTimeProvider _clock = new();
    private readonly DateTimeOffset _start;
    private readonly FakeConnectionCoordinator _coordinator = new();
    private readonly FakeUnlockService _unlock = new();
    private readonly FakeNetworkMonitor _monitor = new();
    private readonly FakeSuccessfulExitService _exit = new();
    private readonly FakeApplicationLifetime _lifetime = new();
    private readonly RecordingLogger<NetworkGate> _logger = new();
    private readonly ResourceLocalizationService _localization = TestLocalization.Create();
    private readonly ConnectNetworkActions _actions;
    private readonly ProvisionedWifiViewModel _provisionedWifi;
    private readonly ScriptedProbe _probe;
    private readonly List<IDisposable> _disposables = [];

    public NetworkGateTests()
    {
        _start = _clock.GetUtcNow();
        _probe = new ScriptedProbe(() => Elapsed);
        _actions = new ConnectNetworkActions(_coordinator, _unlock, new RecordingLogger<ConnectNetworkActions>());
        _provisionedWifi = new ProvisionedWifiViewModel(_localization, _actions, ProvisionedWifiConfiguration());
    }

    private TimeSpan Elapsed => _clock.GetUtcNow() - _start;

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables)
        {
            disposable.Dispose();
        }

        _provisionedWifi.Dispose();
    }

    [Fact]
    public async Task RunAsync_OnlineAtOnce_SkipsConnectImmediatelyThroughTheSuccessfulExit()
    {
        _probe.Script = static _ => InternetStatus.Online;
        NetworkGate gate = CreateGate();

        NetworkGateOutcome outcome = await gate.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(NetworkGateOutcome.SkipConnect, outcome);
        Assert.Equal(TimeSpan.Zero, Elapsed);
        Assert.Equal([TimeSpan.Zero], _probe.Times);
        Assert.Equal([true], _exit.ConnectSkipped);
        RecordedLogEntry entry = Assert.Single(_logger.Entries, static entry => entry.Level == LogLevel.Information);
        Assert.Equal("SkipConnect", entry.Properties["Outcome"]?.ToString());
        Assert.Equal("Online", entry.Properties["Internet"]?.ToString());
        Assert.Equal(0d, entry.Properties["ElapsedSeconds"]);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(14)]
    public async Task RunAsync_OnlineWithinFifteenSeconds_SkipsAtTheFirstProbeThatSeesIt(int onlineAtSeconds)
    {
        _probe.Script = elapsed => elapsed >= TimeSpan.FromSeconds(onlineAtSeconds) ? InternetStatus.Online : InternetStatus.NoInternet;
        NetworkGate gate = CreateGate();

        NetworkGateOutcome outcome = await DriveAsync(gate.RunAsync(TestContext.Current.CancellationToken));

        Assert.Equal(NetworkGateOutcome.SkipConnect, outcome);
        Assert.Equal(TimeSpan.FromSeconds(onlineAtSeconds), Elapsed);
        Assert.Equal(Enumerable.Range(0, (onlineAtSeconds / 2) + 1).Select(static probe => TimeSpan.FromSeconds(probe * 2)), _probe.Times);
        Assert.Equal([true], _exit.ConnectSkipped);
    }

    [Theory]
    [InlineData(InternetStatus.NoInternet)]
    [InlineData(InternetStatus.CaptivePortal)]
    public async Task RunAsync_NotOnlineWithinFifteenSeconds_ShowsConnectAtTheDeadline(InternetStatus beforeSixteenSeconds)
    {
        // Online at 16 s, one second too late.
        _probe.Script = elapsed => elapsed >= TimeSpan.FromSeconds(16) ? InternetStatus.Online : beforeSixteenSeconds;
        NetworkGate gate = CreateGate();

        NetworkGateOutcome outcome = await DriveAsync(gate.RunAsync(TestContext.Current.CancellationToken));

        Assert.Equal(NetworkGateOutcome.ShowConnect, outcome);
        Assert.Equal(NetworkGatePolicy.Duration, Elapsed);
        Assert.Equal(8, _probe.Times.Count);
        Assert.Equal(TimeSpan.FromSeconds(14), _probe.Times[^1]);
        Assert.Equal(0, _exit.Calls);
        RecordedLogEntry entry = Assert.Single(_logger.Entries, static entry => entry.Level == LogLevel.Information);
        Assert.Equal("ShowConnect", entry.Properties["Outcome"]?.ToString());
        Assert.Equal(beforeSixteenSeconds.ToString(), entry.Properties["Internet"]?.ToString());
        Assert.Equal(15d, entry.Properties["ElapsedSeconds"]);
    }

    [Fact]
    public async Task RunAsync_ProbeThatThrows_CountsAsNoInternet()
    {
        _probe.Script = static _ => throw new InvalidOperationException("probe failed");
        NetworkGate gate = CreateGate();

        NetworkGateOutcome outcome = await DriveAsync(gate.RunAsync(TestContext.Current.CancellationToken));

        Assert.Equal(NetworkGateOutcome.ShowConnect, outcome);
        Assert.Equal(NetworkGatePolicy.Duration, Elapsed);
    }

    [Fact]
    public async Task RunAsync_AutoContinueDisabled_ProvisionsThenShowsConnectWithoutProbing()
    {
        NetworkGate gate = CreateGate(isAutoContinueEnabled: false);

        NetworkGateOutcome outcome = await gate.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(NetworkGateOutcome.ShowConnect, outcome);
        Assert.Empty(_probe.Times);
        Assert.Equal(0, _exit.Calls);
        Assert.Equal(1, _coordinator.ApplyProvisioningCalls);
        Assert.Equal(1, _monitor.RunCalls);
    }

    [Theory]
    [InlineData(ConnectUnlockResult.Unlocked)]
    [InlineData(ConnectUnlockResult.Cancelled)]
    public async Task RunAsync_UnlocksWiredThenProvisionsThenStartsTheMonitorThenProbes(ConnectUnlockResult unlockResult)
    {
        _unlock.IsWiredUnlockRequired = true;
        _unlock.Result = unlockResult;
        var order = new List<string>();
        _unlock.OnUnlock = () => order.Add($"unlock provisioning={_coordinator.ApplyProvisioningCalls}");
        _monitor.OnRun = () => order.Add($"monitor provisioning={_coordinator.ApplyProvisioningCalls}");
        _probe.Script = _ =>
        {
            order.Add($"probe monitor={_monitor.RunCalls}");
            return InternetStatus.Online;
        };
        NetworkGate gate = CreateGate();

        await gate.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal([ConnectUnlockTrigger.WiredStartup], _unlock.Calls);
        Assert.Equal(["unlock provisioning=0", "monitor provisioning=1", "probe monitor=1"], order);
    }

    [Fact]
    public async Task RunAsync_RunsOnce()
    {
        _unlock.IsWiredUnlockRequired = true;
        NetworkGate gate = CreateGate(isAutoContinueEnabled: false);
        await gate.RunAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.RunAsync(TestContext.Current.CancellationToken));

        Assert.Single(_unlock.Calls);
        Assert.Equal(1, _coordinator.ApplyProvisioningCalls);
        Assert.Equal(1, _monitor.RunCalls);
    }

    [Fact]
    public async Task StartForRevisitAsync_PreviousOutcome_ReplaysItWithoutProvisioningThenStartsTheMonitor()
    {
        _unlock.IsWiredUnlockRequired = true;
        var previous = new ProvisioningOutcome(
            Dot1xState.NotConfigured,
            new ProvisionedWifiState(ProvisionedWifiStatus.Ready, Ssid: WifiSsid.FromText("Foundry Lab")),
            null,
            [NetworkFailureCode.WifiProfileRejected]);
        NetworkGate gate = CreateGate();

        await gate.StartForRevisitAsync(previous, TestContext.Current.CancellationToken);

        Assert.Equal(0, _coordinator.ApplyProvisioningCalls);
        Assert.Same(previous, Assert.Single(_coordinator.RestoredProvisioning));
        Assert.Equal(_localization.GetString("Wifi.ActionApplyProvisionedFailed"), _provisionedWifi.FeedbackText);
        Assert.Equal(1, _monitor.RunCalls);
        Assert.Empty(_unlock.Calls);
        Assert.Empty(_probe.Times);
        Assert.Equal(0, _exit.Calls);
    }

    [Fact]
    public async Task StartForRevisitAsync_NoPreviousOutcome_ProvisionsOnceWithoutTheWiredPromptOrAProbe()
    {
        _unlock.IsWiredUnlockRequired = true;
        NetworkGate gate = CreateGate();

        await gate.StartForRevisitAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal(1, _coordinator.ApplyProvisioningCalls);
        Assert.Empty(_coordinator.RestoredProvisioning);
        Assert.Equal(1, _monitor.RunCalls);
        Assert.Empty(_unlock.Calls);
        Assert.Empty(_probe.Times);
        Assert.Equal(0, _exit.Calls);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public async Task StartForRevisitAsync_LockedPreviousWifi_RefreshesItOnlyWhenTheSecretsAreUnlocked(bool isWifiStillLocked, int expectedRefreshes)
    {
        _unlock.IsWifiUnlockRequired = isWifiStillLocked;
        var previous = new ProvisioningOutcome(Dot1xState.NotConfigured, new ProvisionedWifiState(ProvisionedWifiStatus.Locked), null, []);
        NetworkGate gate = CreateGate();

        await gate.StartForRevisitAsync(previous, TestContext.Current.CancellationToken);

        Assert.Equal(expectedRefreshes, _coordinator.RefreshProvisionedWifiCalls);
        Assert.Equal(0, _coordinator.ApplyProvisioningCalls);
        Assert.Empty(_unlock.Calls);
    }

    [Fact]
    public async Task StartForRevisitAsync_RefreshThatRejectsTheProfile_ShowsTheFeedbackOfTheMergedOutcome()
    {
        var previous = new ProvisioningOutcome(Dot1xState.NotConfigured, new ProvisionedWifiState(ProvisionedWifiStatus.Locked), null, []);
        _coordinator.RefreshedProvisioning = previous with
        {
            Wifi = new ProvisionedWifiState(ProvisionedWifiStatus.Ready, Ssid: WifiSsid.FromText("Foundry Lab")),
            Failures = [NetworkFailureCode.WifiProfileRejected]
        };
        NetworkGate gate = CreateGate();

        await gate.StartForRevisitAsync(previous, TestContext.Current.CancellationToken);

        Assert.Equal(1, _coordinator.RefreshProvisionedWifiCalls);
        Assert.Equal(_localization.GetString("Wifi.ActionApplyProvisionedFailed"), _provisionedWifi.FeedbackText);
    }

    [Fact]
    public async Task StartForRevisitAsync_RunsOnce()
    {
        NetworkGate gate = CreateGate();
        await gate.StartForRevisitAsync(null, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.StartForRevisitAsync(null, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.RunAsync(TestContext.Current.CancellationToken));

        Assert.Equal(1, _coordinator.ApplyProvisioningCalls);
        Assert.Equal(1, _monitor.RunCalls);
    }

    [Fact]
    public async Task RunAsync_ShowsTheProvisionedWifiFeedback()
    {
        _coordinator.ProvisioningResult = new ProvisioningOutcome(
            Dot1xState.NotConfigured,
            new ProvisionedWifiState(ProvisionedWifiStatus.Ready, Ssid: WifiSsid.FromText("Foundry Lab")),
            null,
            [NetworkFailureCode.WifiProfileRejected]);
        NetworkGate gate = CreateGate(isAutoContinueEnabled: false);

        await gate.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(_localization.GetString("Wifi.ActionApplyProvisionedFailed"), _provisionedWifi.FeedbackText);
    }

    [Fact]
    public async Task RunAsync_ProvisionedWifiOnlineThroughTheMonitorBeforeFifteenSeconds_SkipsConnect()
    {
        // The provisioned profile only connects through the monitor-driven automatic attempt; the internet answers once it did.
        TimeSpan? monitorStartedAt = null;
        _monitor.OnRun = () => monitorStartedAt = Elapsed;
        _probe.Script = _ => _coordinator.State.Wifi.Connection is { Source: WifiConnectionSource.Provisioned } ? InternetStatus.Online : InternetStatus.NoInternet;
        NetworkGate gate = CreateGate();

        NetworkGateOutcome outcome = await DriveAsync(
            gate.RunAsync(TestContext.Current.CancellationToken),
            elapsed =>
            {
                if (elapsed == TimeSpan.FromSeconds(5) && _monitor.RunCalls == 1)
                {
                    _coordinator.Publish(DebugScenarioCatalog.Create(ConnectDebugScenario.WifiReady, _clock.GetUtcNow()));
                }
            });

        Assert.Equal(TimeSpan.Zero, monitorStartedAt);
        Assert.Equal(NetworkGateOutcome.SkipConnect, outcome);
        Assert.Equal(TimeSpan.FromSeconds(6), Elapsed);
        Assert.Equal([true], _exit.ConnectSkipped);
    }

    [Fact]
    public async Task RunAsync_AutomaticWifiPromptOpen_SkipsOnlyOnceItCloses()
    {
        // The page view model exists during the gate and opens the automatic Wi-Fi prompt the monitor's first pass requests.
        _unlock.IsWifiUnlockRequired = true;
        // Inline continuations: the prompt counts as closed as soon as the test completes it.
        _unlock.Gate = new TaskCompletionSource<ConnectUnlockResult>();
        _monitor.OnRun = _coordinator.RaiseUnlockRequested;
        _probe.Script = static _ => InternetStatus.Online;
        using ConnectPageViewModel page = CreatePage();
        NetworkGate gate = CreateGate();
        int exitCallsWhilePromptOpen = -1;

        NetworkGateOutcome outcome = await DriveAsync(
            gate.RunAsync(TestContext.Current.CancellationToken),
            elapsed =>
            {
                if (elapsed == TimeSpan.FromSeconds(3))
                {
                    Assert.True(_unlock.IsPromptOpen);
                    exitCallsWhilePromptOpen = _exit.Calls;
                    _unlock.Gate!.SetResult(ConnectUnlockResult.Unlocked);
                }
            });

        Assert.Equal([ConnectUnlockTrigger.AutomaticWifi], _unlock.Calls);
        Assert.Equal(0, exitCallsWhilePromptOpen);
        Assert.Equal(NetworkGateOutcome.SkipConnect, outcome);
        Assert.Equal(TimeSpan.FromSeconds(4), Elapsed);
        Assert.Equal([true], _exit.ConnectSkipped);
    }

    [Fact]
    public async Task RunAsync_MonitorOnlineWhileTheGateShowsConnect_StartsNoCountdownUntilThePageIsShown()
    {
        // The monitor sees the internet while every gate probe misses it: the hidden page must not count down or leave.
        _monitor.OnRun = () => _coordinator.Publish(DebugScenarioCatalog.Create(ConnectDebugScenario.EthernetReady, _clock.GetUtcNow()));
        _probe.Script = static _ => InternetStatus.NoInternet;
        using ConnectPageViewModel page = CreatePage();
        NetworkGate gate = CreateGate();

        NetworkGateOutcome outcome = await DriveAsync(gate.RunAsync(TestContext.Current.CancellationToken));

        Assert.Equal(NetworkGateOutcome.ShowConnect, outcome);
        Assert.Equal(ConnectPageStatus.Ready, page.Status);
        Assert.False(page.AutoContinue.IsCounting);
        Assert.Equal(0, _exit.Calls);

        page.AutoContinue.OnPageShown();

        Assert.True(page.AutoContinue.IsCounting);
        Assert.Equal(AutoContinuePolicy.CountdownSeconds, page.AutoContinue.SecondsRemaining);
    }

    [Fact]
    public async Task RunAsync_FinalProbeNotOnline_KeepsProbing()
    {
        _probe.Script = static _ => InternetStatus.Online;
        _exit.Results.Enqueue(SuccessfulExitResult.NotOnline);
        NetworkGate gate = CreateGate();

        NetworkGateOutcome outcome = await DriveAsync(gate.RunAsync(TestContext.Current.CancellationToken));

        Assert.Equal(NetworkGateOutcome.SkipConnect, outcome);
        Assert.Equal(TimeSpan.FromSeconds(2), Elapsed);
        Assert.Equal([true, true], _exit.ConnectSkipped);
    }

    [Fact]
    public async Task RunAsync_ExitRequestedDuringTheGate_StopsWithoutLeavingAndStopsTheMonitor()
    {
        _probe.Script = static _ => InternetStatus.NoInternet;
        NetworkGate gate = CreateGate();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DriveAsync(
            gate.RunAsync(TestContext.Current.CancellationToken),
            elapsed =>
            {
                if (elapsed == TimeSpan.FromSeconds(3))
                {
                    _lifetime.TryExit(ChildRuntimeExitCode.UserAborted);
                }
            }));

        Assert.Equal(0, _exit.Calls);
        Assert.True(_monitor.RunToken.IsCancellationRequested);
        Assert.DoesNotContain(_logger.Entries, static entry => entry.Level == LogLevel.Information);
    }

    [Fact]
    public async Task RunAsync_CallerCancelled_StopsWithoutLeaving()
    {
        _probe.Script = static _ => InternetStatus.NoInternet;
        using var cancellation = new CancellationTokenSource();
        NetworkGate gate = CreateGate();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DriveAsync(
            gate.RunAsync(cancellation.Token),
            elapsed =>
            {
                if (elapsed == TimeSpan.FromSeconds(3))
                {
                    cancellation.Cancel();
                }
            }));

        Assert.Equal(0, _exit.Calls);
    }

    [Fact]
    public async Task ExitRequest_StopsTheMonitor()
    {
        NetworkGate gate = CreateGate(isAutoContinueEnabled: false);
        await gate.RunAsync(TestContext.Current.CancellationToken);
        Assert.False(_monitor.RunToken.IsCancellationRequested);

        _lifetime.TryExit(ChildRuntimeExitCode.Success);

        Assert.True(_monitor.RunToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Dispose_StopsTheMonitorAndIsIdempotent()
    {
        NetworkGate gate = CreateGate(isAutoContinueEnabled: false);
        await gate.RunAsync(TestContext.Current.CancellationToken);

        gate.Dispose();
        gate.Dispose();

        Assert.True(_monitor.RunToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Dispose_DuringTheWiredPrompt_NeverProvisionsNorStartsTheMonitor()
    {
        _unlock.IsWiredUnlockRequired = true;
        NetworkGate gate = CreateGate();
        _unlock.OnUnlock = gate.Dispose;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.RunAsync(TestContext.Current.CancellationToken));

        Assert.Equal(0, _coordinator.ApplyProvisioningCalls);
        Assert.Equal(0, _monitor.RunCalls);
    }

    [Fact]
    public async Task ExitRequest_WhileProvisioningIgnoresCancellation_NeverStartsTheMonitor()
    {
        _coordinator.ProvisioningIgnoresCancellation = true;
        _coordinator.ProvisioningGate = new TaskCompletionSource<ProvisioningOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        NetworkGate gate = CreateGate();
        Task<NetworkGateOutcome> run = gate.RunAsync(TestContext.Current.CancellationToken);

        _lifetime.TryExit(ChildRuntimeExitCode.UserAborted);
        _coordinator.ProvisioningGate.SetResult(ProvisioningOutcome.NotConfigured);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(0, _monitor.RunCalls);
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

    // Not inlined: once it returns, nothing in the test references the gate, the monitor or the faulted task.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<WeakReference> StartAndDisposeWithFaultedMonitorAsync(Exception fault)
    {
        var monitor = new FakeNetworkMonitor { RunResult = Task.FromException(fault) };
        var gate = new NetworkGate(
            _actions, _coordinator, monitor, _probe, _unlock, _exit, _lifetime, _provisionedWifi, _clock, _logger, isAutoContinueEnabled: false);
        await gate.RunAsync(TestContext.Current.CancellationToken);
        var monitorTask = new WeakReference(monitor.RunResult);
        gate.Dispose();
        return monitorTask;
    }

    /// <summary>
    /// Advances the clock one second at a time, each time once the gate waits for its next probe (the deadline and the
    /// probe delay armed), until the gate ends; <paramref name="afterSecond"/> runs after each advance.
    /// </summary>
    private async Task<NetworkGateOutcome> DriveAsync(Task<NetworkGateOutcome> run, Action<TimeSpan>? afterSecond = null)
    {
        while (true)
        {
            await AsyncAssert.EventuallyAsync(() => run.IsCompleted || _clock.ActiveTimerCount == 2, "the gate waits for its next probe");
            if (run.IsCompleted)
            {
                return await run;
            }

            Assert.True(Elapsed < TimeSpan.FromSeconds(20), "the gate ends within its deadline");
            _clock.Advance(TimeSpan.FromSeconds(1));
            afterSecond?.Invoke(Elapsed);
        }
    }

    private NetworkGate CreateGate(bool isAutoContinueEnabled = true)
    {
        var gate = new NetworkGate(
            _actions, _coordinator, _monitor, _probe, _unlock, _exit, _lifetime, _provisionedWifi, _clock, _logger, isAutoContinueEnabled);
        _disposables.Add(gate);
        return gate;
    }

    private ConnectPageViewModel CreatePage()
    {
        var dispatcher = new ImmediateUiDispatcher();
        return new ConnectPageViewModel(
            _localization, _coordinator, _monitor, _actions,
            new AutoContinueViewModel(
                _localization, _exit, _lifetime, _clock, dispatcher, new RecordingLogger<AutoContinueViewModel>(), isAutoContinueEnabled: true, isPageShown: false),
            new EthernetSectionViewModel(_localization),
            _provisionedWifi,
            new WifiNetworksViewModel(_localization, _actions, _monitor),
            new DiagnosticsViewModel(_localization, new RecordingLogger<DiagnosticsViewModel>()),
            dispatcher, _clock, new RecordingLogger<ConnectPageViewModel>());
    }

    private static FoundryConnectConfigurationDocument ProvisionedWifiConfiguration() => new()
    {
        Capabilities = new ConnectNetworkCapabilitiesSettings { WifiProvisioned = true },
        Wifi = new ConnectWifiSettings { IsEnabled = true, Ssid = "Foundry Lab", SecurityType = "WPA3-Personal" }
    };

    /// <summary>Answers each check from <see cref="Script"/> with the elapsed time and records when it was called.</summary>
    private sealed class ScriptedProbe(Func<TimeSpan> elapsed) : IConnectivityProbe
    {
        public Func<TimeSpan, InternetStatus> Script { get; set; } = static _ => InternetStatus.NoInternet;

        public List<TimeSpan> Times { get; } = [];

        public Task<InternetStatus> CheckAsync(CancellationToken cancellationToken)
        {
            TimeSpan now = elapsed();
            Times.Add(now);
            try
            {
                return Task.FromResult(Script(now));
            }
            catch (Exception exception)
            {
                return Task.FromException<InternetStatus>(exception);
            }
        }
    }
}
