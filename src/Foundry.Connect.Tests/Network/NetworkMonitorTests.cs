// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Connect.Tests.Support;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Tests.Network;

public sealed class NetworkMonitorTests
{
    private readonly ManualTimeProvider _clock = new();

    [Fact]
    public async Task RunAsync_ObservesOnStartThenEveryInterval()
    {
        var source = new CountingSource(_clock);
        var coordinator = new RecordingCoordinator(_clock);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        NetworkMonitor monitor = CreateMonitor(source, coordinator);

        Task run = monitor.RunAsync(cancellation.Token);
        await AsyncAssert.EventuallyAsync(() => coordinator.Observations.Count == 1 && _clock.ActiveTimerCount == 1, "first observation, then the interval timer is armed");
        _clock.Advance(TimeSpan.FromSeconds(9));
        Assert.Single(coordinator.Observations);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await AsyncAssert.EventuallyAsync(() => coordinator.Observations.Count == 2, "second observation after the interval");

        await cancellation.CancelAsync();
        await run;
    }

    [Fact]
    public async Task RunAsync_RefreshesImmediatelyWhenRequested()
    {
        var source = new CountingSource(_clock);
        var coordinator = new RecordingCoordinator(_clock);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        NetworkMonitor monitor = CreateMonitor(source, coordinator);

        Task run = monitor.RunAsync(cancellation.Token);
        await AsyncAssert.EventuallyAsync(() => coordinator.Observations.Count == 1 && _clock.ActiveTimerCount == 1, "first observation, then the interval timer is armed");
        monitor.RequestRefresh();
        await AsyncAssert.EventuallyAsync(() => coordinator.Observations.Count == 2, "refresh on request without waiting for the interval");

        await cancellation.CancelAsync();
        await run;
    }

    [Fact]
    public async Task RunAsync_AfterARequestedRefresh_WaitsAFullIntervalAgain()
    {
        var source = new CountingSource(_clock);
        var coordinator = new RecordingCoordinator(_clock);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        NetworkMonitor monitor = CreateMonitor(source, coordinator);

        Task run = monitor.RunAsync(cancellation.Token);
        await AsyncAssert.EventuallyAsync(() => coordinator.Observations.Count == 1 && _clock.ActiveTimerCount == 1, "first observation, then the interval timer is armed");
        monitor.RequestRefresh();
        await AsyncAssert.EventuallyAsync(() => coordinator.Observations.Count == 2 && _clock.ActiveTimerCount == 1, "refresh, then a new interval timer");
        _clock.Advance(TimeSpan.FromSeconds(9));
        Assert.Equal(2, coordinator.Observations.Count);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await AsyncAssert.EventuallyAsync(() => coordinator.Observations.Count == 3, "next observation one interval after the refresh");

        await cancellation.CancelAsync();
        await run;
    }

    [Fact]
    public async Task RunAsync_ReadsWiredAuthenticationOnceTheProfileIsApplied()
    {
        var source = new CountingSource(_clock);
        var coordinator = new RecordingCoordinator(_clock);
        coordinator.State = coordinator.State with { Ethernet = EthernetState.Empty with { Dot1x = new Dot1xState(Dot1xAuthenticationStatus.ProfileApplied) } };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        NetworkMonitor monitor = CreateMonitor(source, coordinator);

        Task run = monitor.RunAsync(cancellation.Token);
        await AsyncAssert.EventuallyAsync(() => source.WiredFlags.Count == 1, "first observation on start");

        Assert.True(source.WiredFlags[0]);
        await cancellation.CancelAsync();
        await run;
    }

    [Fact]
    public async Task RunAsync_DoesNotPublishCheckingWhileReprobingAKnownState()
    {
        var source = new CountingSource(_clock);
        var coordinator = new RecordingCoordinator(_clock);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        NetworkMonitor monitor = CreateMonitor(source, coordinator);

        Task run = monitor.RunAsync(cancellation.Token);
        await AsyncAssert.EventuallyAsync(() => coordinator.Observations.Count == 1 && _clock.ActiveTimerCount == 1, "first observation, then the interval timer is armed");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Gate = gate;
        _clock.Advance(TimeSpan.FromSeconds(10));
        await AsyncAssert.EventuallyAsync(() => source.Calls == 2, "second probe in flight");

        // The internet status of the first observation stays published until the new probe answers.
        Assert.Single(coordinator.Observations);
        gate.SetResult();
        await AsyncAssert.EventuallyAsync(() => coordinator.Observations.Count == 2, "second observation after the probe answered");

        Assert.All(coordinator.Observations, static observation => Assert.Equal(InternetStatus.Online, observation.Internet));
        await cancellation.CancelAsync();
        await run;
    }

    [Fact]
    public async Task RunAsync_WhenAnObservationFails_KeepsRunningAndLogsTheFailureOnce()
    {
        var source = new CountingSource(_clock);
        var coordinator = new RecordingCoordinator(_clock) { ApplyException = new InvalidOperationException("boom") };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var logger = new RecordingLogger<NetworkMonitor>();
        var monitor = new NetworkMonitor(source, coordinator, new NetworkRefreshSignal(), new ConnectNetworkOptions(true, TimeSpan.FromSeconds(10)), _clock, logger);

        Task run = monitor.RunAsync(cancellation.Token);
        await AsyncAssert.EventuallyAsync(() => source.Calls == 1 && _clock.ActiveTimerCount == 1, "first pass failed, interval timer armed");
        _clock.Advance(TimeSpan.FromSeconds(10));
        await AsyncAssert.EventuallyAsync(() => source.Calls == 2 && _clock.ActiveTimerCount == 1, "second pass after a failed one");

        await cancellation.CancelAsync();
        await run;
        RecordedLogEntry error = Assert.Single(logger.Entries, static entry => entry.Level == LogLevel.Error);
        Assert.Equal(nameof(InvalidOperationException), error.Properties["FailureType"]);
        Assert.Null(error.Exception);
        Assert.Contains(logger.Entries, static entry => entry.Level == LogLevel.Debug && entry.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task RunAsync_WhenAFailedPassIsFollowedByASuccess_LogsRecoveryOnce()
    {
        var source = new CountingSource(_clock);
        var coordinator = new RecordingCoordinator(_clock) { ApplyException = new InvalidOperationException("boom") };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var logger = new RecordingLogger<NetworkMonitor>();
        var monitor = new NetworkMonitor(source, coordinator, new NetworkRefreshSignal(), new ConnectNetworkOptions(true, TimeSpan.FromSeconds(10)), _clock, logger);

        Task run = monitor.RunAsync(cancellation.Token);
        await AsyncAssert.EventuallyAsync(() => source.Calls == 1 && _clock.ActiveTimerCount == 1, "first pass failed, interval timer armed");
        coordinator.ApplyException = null;
        _clock.Advance(TimeSpan.FromSeconds(10));
        await AsyncAssert.EventuallyAsync(() => coordinator.Observations.Count == 1 && _clock.ActiveTimerCount == 1, "second pass succeeded");
        _clock.Advance(TimeSpan.FromSeconds(10));
        await AsyncAssert.EventuallyAsync(() => coordinator.Observations.Count == 2, "third pass succeeded");

        await cancellation.CancelAsync();
        await run;
        RecordedLogEntry recovered = Assert.Single(logger.Entries, static entry => entry.Message == "Network monitor pass recovered.");
        Assert.Equal(LogLevel.Information, recovered.Level);
        Assert.Null(recovered.Exception);
    }

    [Fact]
    public async Task RunAsync_RunsTheFirstPassOffTheCallingThread()
    {
        var source = new CountingSource(_clock);
        var coordinator = new RecordingCoordinator(_clock);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        NetworkMonitor monitor = CreateMonitor(source, coordinator);

        // A dedicated thread is never a pool thread, so it cannot be the one that later runs the loop.
        Task? run = null;
        int callerThreadId = 0;
        var caller = new Thread(() =>
        {
            callerThreadId = Environment.CurrentManagedThreadId;
            run = monitor.RunAsync(cancellation.Token);
        });
        caller.Start();
        caller.Join();
        await AsyncAssert.EventuallyAsync(() => source.Calls == 1, "first observation");

        Assert.NotEqual(callerThreadId, source.FirstObserveThreadId);
        await cancellation.CancelAsync();
        await run!;
    }

    private NetworkMonitor CreateMonitor(INetworkObservationSource source, IConnectionCoordinator coordinator) =>
        new(source, coordinator, new NetworkRefreshSignal(), new ConnectNetworkOptions(true, TimeSpan.FromSeconds(10)), _clock, new RecordingLogger<NetworkMonitor>());

    private sealed class CountingSource(TimeProvider clock) : INetworkObservationSource
    {
        private readonly object _gate = new();
        private readonly List<bool> _wiredFlags = [];
        private int _calls;
        private int _firstThreadId;

        // When set, the next observation waits for it before returning.
        public TaskCompletionSource? Gate { get; set; }

        public int Calls => Volatile.Read(ref _calls);

        public int FirstObserveThreadId => Volatile.Read(ref _firstThreadId);

        public IReadOnlyList<bool> WiredFlags
        {
            get
            {
                lock (_gate)
                {
                    return [.. _wiredFlags];
                }
            }
        }

        public async Task<NetworkObservation> ObserveAsync(bool readWiredAuthentication, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _wiredFlags.Add(readWiredAuthentication);
            }

            Interlocked.CompareExchange(ref _firstThreadId, Environment.CurrentManagedThreadId, 0);
            Interlocked.Increment(ref _calls);
            if (Gate is { } gate)
            {
                await gate.Task.WaitAsync(cancellationToken);
            }

            return new NetworkObservation([], WifiObservation.Disabled, InternetStatus.Online, null, clock.GetUtcNow());
        }
    }

    private sealed class RecordingCoordinator(TimeProvider clock) : IConnectionCoordinator
    {
        private readonly object _gate = new();
        private readonly List<NetworkObservation> _observations = [];

        public NetworkState State { get; set; } = NetworkState.Initial(clock.GetUtcNow());

        public Exception? ApplyException { get; set; }

        public IReadOnlyList<NetworkObservation> Observations
        {
            get
            {
                lock (_gate)
                {
                    return [.. _observations];
                }
            }
        }

        public event EventHandler<NetworkState>? StateChanged
        {
            add { }
            remove { }
        }

        public Task ApplyObservationAsync(NetworkObservation observation, CancellationToken cancellationToken)
        {
            if (ApplyException is { } exception)
            {
                return Task.FromException(exception);
            }

            lock (_gate)
            {
                _observations.Add(observation);
            }

            return Task.CompletedTask;
        }

        public Task<ProvisioningOutcome> ApplyProvisioningAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OperationOutcome> ConnectProvisionedWifiAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OperationOutcome> ConnectWifiNetworkAsync(WifiSsid ssid, string? passphrase, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OperationOutcome> DisconnectWifiAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public void ReleaseProvisionedProfile() => throw new NotSupportedException();
    }
}
