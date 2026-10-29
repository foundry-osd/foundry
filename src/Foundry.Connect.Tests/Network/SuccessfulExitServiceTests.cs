// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net.Http;
using Foundry.Connect.Configuration;
using Foundry.Connect.DependencyInjection;
using Foundry.Connect.Lifetime;
using Foundry.Connect.Network;
using Foundry.Connect.Tests.Support;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Runtime;
using Foundry.Telemetry;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Tests.Network;

public sealed class SuccessfulExitServiceTests
{
    private readonly ManualTimeProvider _clock = new();
    private readonly RecordingLifetime _lifetime = new();
    private readonly HangingTelemetry _telemetry = new();
    private readonly RecordingRoamingCapture _roaming = new();
    private readonly NetworkRefreshSignal _refresh = new();
    private readonly RecordingLogger<SuccessfulExitService> _logger = new();
    private readonly StateCoordinator _coordinator;
    private readonly RecordingPreferencesPublisher _preferences;
    private readonly ConnectRuntimeSecrets _secrets = new(ConnectSecret.Available("a".ToCharArray()), ConnectSecret.NotProvisioned(), ConnectSecret.NotProvisioned());

    public SuccessfulExitServiceTests()
    {
        _preferences = new RecordingPreferencesPublisher(_lifetime);
        _coordinator = new StateCoordinator(NetworkState.Initial(_clock.GetUtcNow()) with { Internet = InternetStatus.Online });
    }

    [Fact]
    public async Task TryComplete_WhenTelemetryHangs_ExitsAfterTheTwoSecondBudget()
    {
        SuccessfulExitService service = Create(InternetStatus.Online);

        Task<SuccessfulExitResult> exit = service.TryCompleteAsync(TestContext.Current.CancellationToken);
        await AsyncAssert.EventuallyAsync(() => _telemetry.Calls == 1 && _clock.ActiveTimerCount > 0, "telemetry was sent with a budget timer");
        Assert.False(exit.IsCompleted);
        _clock.Advance(SuccessfulExitService.TelemetryBudget);

        Assert.Equal(SuccessfulExitResult.Exited, await exit.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal([ChildRuntimeExitCode.Success], _lifetime.Codes);
        Assert.Equal(1, _roaming.Calls);
        Assert.Equal(1, _coordinator.ReleaseCalls);
        Assert.Equal(ConnectSecretStatus.Cleared, _secrets.WifiPassphrase.Status);
    }

    [Fact]
    public async Task TryComplete_SendsTheSessionReadyEventBuiltFromTheTypedState()
    {
        _telemetry.CompleteImmediately = true;

        await Create(InternetStatus.Online).TryCompleteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TelemetryEvents.ConnectSessionReady, _telemetry.LastEventName);
        Assert.NotNull(_telemetry.LastProperties);
        Assert.Equal("unknown", _telemetry.LastProperties["connect_network_connection_type"]);
    }

    [Theory]
    [InlineData(InternetStatus.CaptivePortal)]
    [InlineData(InternetStatus.NoInternet)]
    public async Task TryComplete_WhenFinalProbeIsNotOnline_StaysAndRequestsRefresh(InternetStatus finalProbe)
    {
        SuccessfulExitService service = Create(finalProbe);

        SuccessfulExitResult result = await service.TryCompleteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SuccessfulExitResult.NotOnline, result);
        Assert.Empty(_lifetime.Codes);
        Assert.Equal(0, _telemetry.Calls);
        Assert.Equal(0, _roaming.Calls);
        Assert.Equal(0, _coordinator.ReleaseCalls);
        Assert.Equal(0, _preferences.Calls);
        Assert.Equal(ConnectSecretStatus.Available, _secrets.WifiPassphrase.Status);
        await _refresh.WaitAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TryComplete_WhenFinalProbeThrows_StaysAsNoInternet()
    {
        SuccessfulExitService service = Create(new StubProbe(new InvalidOperationException("probe failed")));

        SuccessfulExitResult result = await service.TryCompleteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SuccessfulExitResult.NotOnline, result);
        Assert.Empty(_lifetime.Codes);
    }

    [Fact]
    public async Task TryComplete_WhenRoamingFails_StillExits()
    {
        _roaming.Failure = new IOException("disk full");
        _telemetry.CompleteImmediately = true;

        SuccessfulExitResult result = await Create(InternetStatus.Online).TryCompleteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SuccessfulExitResult.Exited, result);
        Assert.Equal([ChildRuntimeExitCode.Success], _lifetime.Codes);
        Assert.Contains(_logger.Entries, static entry => entry.Level == LogLevel.Warning);
        Assert.DoesNotContain(_logger.Entries, static entry => entry.Message.Contains("disk full", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TryComplete_WhenTelemetryThrows_StillExits()
    {
        _telemetry.Failure = new HttpRequestException("offline");

        SuccessfulExitResult result = await Create(InternetStatus.Online).TryCompleteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SuccessfulExitResult.Exited, result);
        Assert.Equal([ChildRuntimeExitCode.Success], _lifetime.Codes);
    }

    [Fact]
    public async Task TryComplete_WhenCallerCancels_ThrowsAndStays()
    {
        using var cancellation = new CancellationTokenSource();
        SuccessfulExitService service = Create(InternetStatus.Online);

        Task<SuccessfulExitResult> exit = service.TryCompleteAsync(cancellation.Token);
        await AsyncAssert.EventuallyAsync(() => _telemetry.Calls == 1, "telemetry was sent");
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exit);
        Assert.Empty(_lifetime.Codes);
    }

    [Fact]
    public async Task TryComplete_WhenExitAlreadyRequested_DoesNothing()
    {
        _lifetime.TryExit(ChildRuntimeExitCode.UserAborted);

        SuccessfulExitResult result = await Create(InternetStatus.Online).TryCompleteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SuccessfulExitResult.AlreadyExiting, result);
        Assert.Equal(0, _telemetry.Calls);
    }

    [Fact]
    public async Task TryComplete_LogsNoWifiIdentity()
    {
        _telemetry.CompleteImmediately = true;
        _coordinator.State = _coordinator.State with
        {
            Wifi = WifiState.Initial with
            {
                Availability = WifiAvailability.Available,
                Connection = new WifiConnection(Guid.NewGuid(), WifiSsid.FromText("SecretLab"), "SecretLabProfile", WifiSecurityType.Wpa2Personal)
            }
        };

        await Create(InternetStatus.Online).TryCompleteAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(_logger.Entries);
        foreach (RecordedLogEntry entry in _logger.Entries)
        {
            Assert.DoesNotContain("SecretLab", entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(entry.Properties.Values, static value => value?.ToString()?.Contains("SecretLab", StringComparison.Ordinal) == true);
        }
    }

    [Fact]
    public async Task TryComplete_PublishesPreferencesBeforeExit()
    {
        _telemetry.CompleteImmediately = true;

        SuccessfulExitResult result = await Create(InternetStatus.Online).TryCompleteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SuccessfulExitResult.Exited, result);
        Assert.Equal(1, _preferences.Calls);
        Assert.Equal([0], _preferences.ExitCodesSeenAtPublish);
    }

    [Fact]
    public async Task TryComplete_WhenPreferencesPublishThrows_StillExits()
    {
        _telemetry.CompleteImmediately = true;
        _preferences.Failure = new InvalidOperationException("boom");

        SuccessfulExitResult result = await Create(InternetStatus.Online).TryCompleteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SuccessfulExitResult.Exited, result);
        Assert.Equal([ChildRuntimeExitCode.Success], _lifetime.Codes);
        Assert.Contains(_logger.Entries, static entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public void TelemetryHttpClient_HasAnExplicitTimeout()
    {
        using HttpClient client = ServiceCollectionExtensions.CreateTelemetryHttpClient();

        Assert.Equal(TimeSpan.FromSeconds(10), client.Timeout);
    }

    private SuccessfulExitService Create(InternetStatus finalProbe) => Create(new StubProbe(finalProbe));

    private SuccessfulExitService Create(StubProbe probe) => new(
        probe,
        _coordinator,
        _roaming,
        _telemetry,
        new FoundryConnectConfigurationDocument(),
        _secrets,
        _lifetime,
        _refresh,
        _preferences,
        _clock,
        _logger);

    private sealed class StubProbe : IConnectivityProbe
    {
        private readonly InternetStatus _status;
        private readonly Exception? _failure;

        public StubProbe(InternetStatus status) => _status = status;

        public StubProbe(Exception failure) => _failure = failure;

        public Task<InternetStatus> CheckAsync(CancellationToken cancellationToken) =>
            _failure is null ? Task.FromResult(_status) : Task.FromException<InternetStatus>(_failure);
    }

    private sealed class StateCoordinator(NetworkState state) : IConnectionCoordinator
    {
        public NetworkState State { get; set; } = state;

        public int ReleaseCalls { get; private set; }

        public event EventHandler<NetworkState>? StateChanged
        {
            add { }
            remove { }
        }

        public void ReleaseProvisionedProfile() => ReleaseCalls++;

        public Task<ProvisioningOutcome> ApplyProvisioningAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ApplyObservationAsync(NetworkObservation observation, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OperationOutcome> ConnectProvisionedWifiAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OperationOutcome> ConnectWifiNetworkAsync(WifiSsid ssid, string? passphrase, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OperationOutcome> DisconnectWifiAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingRoamingCapture : IRoamingCapture
    {
        public int Calls { get; private set; }

        public Exception? Failure { get; set; }

        public Task CaptureAsync(NetworkState state, CancellationToken cancellationToken)
        {
            Calls++;
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    private sealed class HangingTelemetry : ITelemetryService
    {
        public int Calls { get; private set; }

        public bool CompleteImmediately { get; set; }

        public Exception? Failure { get; set; }

        public string? LastEventName { get; private set; }

        public IReadOnlyDictionary<string, object?>? LastProperties { get; private set; }

        public async Task TrackAsync(string eventName, IReadOnlyDictionary<string, object?> properties, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastEventName = eventName;
            LastProperties = properties;
            if (Failure is not null)
            {
                throw Failure;
            }

            if (CompleteImmediately)
            {
                return;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }

        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingPreferencesPublisher(RecordingLifetime lifetime) : IRuntimePreferencesPublisher
    {
        public int Calls { get; private set; }

        public List<int> ExitCodesSeenAtPublish { get; } = [];

        public Exception? Failure { get; set; }

        public void Publish()
        {
            Calls++;
            ExitCodesSeenAtPublish.Add(lifetime.Codes.Count);
            if (Failure is not null)
            {
                throw Failure;
            }
        }
    }

    private sealed class RecordingLifetime : IApplicationLifetimeService
    {
        private readonly CancellationTokenSource _exitRequested = new();

        public List<ChildRuntimeExitCode> Codes { get; } = [];

        public ChildRuntimeExitCode? RequestedExitCode { get; private set; }

        public CancellationToken ExitRequested => _exitRequested.Token;

        public bool TryExit(ChildRuntimeExitCode exitCode)
        {
            if (RequestedExitCode is not null)
            {
                return false;
            }

            RequestedExitCode = exitCode;
            Codes.Add(exitCode);
            _exitRequested.Cancel();
            return true;
        }
    }
}
