// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Connect.Network;
using Foundry.Connect.Network.Native;
using Foundry.Connect.Network.Policy;
using Foundry.Connect.Tests.Support;
using Foundry.Core.Models.Configuration;
using Foundry.Telemetry;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Foundry.Connect.Tests.Network;

public sealed class ConnectionCoordinatorTests
{
    private static readonly Guid WifiInterface = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private readonly ManualTimeProvider _clock = new();
    private readonly FakeWlanClient _wlan = new();
    private readonly RecordingLogger<ConnectionCoordinator> _logger = new();
    private CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ApplyObservation_AutoConnectsProvisionedWhenPolicyAllows()
    {
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(ReadyLab());

        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, connectedSsid: null, "Lab"), Token);

        Assert.Equal([(WifiInterface, "Lab")], _wlan.ConnectCalls);
        Assert.Single(_wlan.SetProfileCalls);
        Assert.Equal(ConnectionMode.Automatic, coordinator.State.Mode);
        Assert.True(coordinator.State.LastOutcome!.IsSuccess);
    }

    [Fact]
    public async Task ManualRequest_SwitchesToManualBeforeWaitingForRunningOperation()
    {
        _wlan.ConnectGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(ReadyLab());

        Task automatic = coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Lab", "Guest"), Token);
        await AsyncAssert.EventuallyAsync(() => _wlan.ConnectCalls.Count == 1, "the automatic attempt has started");
        Task<OperationOutcome> manual = coordinator.ConnectWifiNetworkAsync(WifiSsid.FromText("Guest"), "guest passphrase", Token);

        Assert.Equal(ConnectionMode.Manual, coordinator.State.Mode);
        Assert.False(manual.IsCompleted);

        _wlan.ConnectGate.SetResult();
        await automatic;
        Assert.True((await manual).IsSuccess);
        Assert.Equal(1, _wlan.MaxConcurrentOperations);
        Assert.Equal(["Lab", "Guest"], _wlan.ConnectCalls.Select(static call => call.ProfileName));

        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Lab"), Token);
        Assert.Equal(2, _wlan.ConnectCalls.Count);
    }

    [Fact]
    public async Task ManualDisconnect_StopsAutomaticAttempts()
    {
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(ReadyLab());
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.Online, connectedSsid: "Lab", "Lab"), Token);

        OperationOutcome outcome = await coordinator.DisconnectWifiAsync(Token);
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Lab"), Token);

        Assert.True(outcome.IsSuccess);
        Assert.Equal([WifiInterface], _wlan.DisconnectCalls);
        Assert.Empty(_wlan.ConnectCalls);
        Assert.Equal(ConnectionMode.Manual, coordinator.State.Mode);
    }

    [Fact]
    public async Task ApplyObservation_RespectsBackoffAfterFailure()
    {
        _wlan.ConnectResults.Enqueue(new WlanOperationResult(WlanOperationStatus.ConnectionFailed, 0, 0x00038005));
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(ReadyLab());

        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Lab"), Token);
        _clock.Advance(TimeSpan.FromSeconds(5));
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Lab"), Token);
        Assert.Single(_wlan.ConnectCalls);
        Assert.Equal(NetworkFailureCode.ConnectionFailed, coordinator.State.LastOutcome!.Failure);
        Assert.Equal(0x00038005u, coordinator.State.LastOutcome.WlanReasonCode);

        _clock.Advance(TimeSpan.FromSeconds(15));
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Lab"), Token);
        Assert.Equal(2, _wlan.ConnectCalls.Count);
    }

    [Fact]
    public async Task ApplyObservation_HiddenProvisionedProfile_ConnectsWithoutVisibility()
    {
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(ReadyLab(hidden: true));

        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, connectedSsid: null), Token);

        Assert.Equal([(WifiInterface, "Lab")], _wlan.ConnectCalls);
    }

    [Theory]
    [InlineData(WifiSecurityType.Wpa2Enterprise, "correct horse", NetworkFailureCode.UnsupportedSecurity)]
    [InlineData(WifiSecurityType.Wep, "correct horse", NetworkFailureCode.UnsupportedSecurity)]
    [InlineData(WifiSecurityType.Wpa2Personal, "short", NetworkFailureCode.InvalidPassphrase)]
    public async Task ConnectWifiNetwork_RejectsBeforeAnyWlanCall(WifiSecurityType security, string passphrase, NetworkFailureCode expected)
    {
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(ProvisioningOutcome.NotConfigured);
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, security, "Guest"), Token);

        OperationOutcome outcome = await coordinator.ConnectWifiNetworkAsync(WifiSsid.FromText("Guest"), passphrase, Token);

        Assert.Equal(expected, outcome.Failure);
        Assert.Empty(_wlan.SetProfileCalls);
        Assert.Empty(_wlan.ConnectCalls);
    }

    [Fact]
    public async Task ConnectWifiNetwork_WhenNotVisible_ReturnsNetworkNotVisible()
    {
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(ProvisioningOutcome.NotConfigured);
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Guest"), Token);

        OperationOutcome outcome = await coordinator.ConnectWifiNetworkAsync(WifiSsid.FromText("GUEST"), "guest passphrase", Token);

        Assert.Equal(NetworkFailureCode.NetworkNotVisible, outcome.Failure);
    }

    [Fact]
    public async Task ConnectWifiNetwork_WhenProfileIsRejected_KeepsReasonCode()
    {
        _wlan.SetProfileResult = new WlanOperationResult(WlanOperationStatus.ProfileRejected, 1206, 0x00080005);
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(ProvisioningOutcome.NotConfigured);
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Guest"), Token);

        OperationOutcome outcome = await coordinator.ConnectWifiNetworkAsync(WifiSsid.FromText("Guest"), "guest passphrase", Token);

        Assert.Equal(OperationOutcome.Failed(NetworkOperationKind.ConnectWifiNetwork, NetworkFailureCode.WifiProfileRejected, 0x00080005), outcome);
        Assert.Empty(_wlan.ConnectCalls);
    }

    [Fact]
    public async Task ConnectWifiNetwork_WhenFeatureIsDisabled_ReturnsWifiFeatureDisabled()
    {
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(ProvisioningOutcome.NotConfigured, wifiEnabled: false);

        OperationOutcome outcome = await coordinator.ConnectWifiNetworkAsync(WifiSsid.FromText("Guest"), "guest passphrase", Token);

        Assert.Equal(NetworkFailureCode.WifiFeatureDisabled, outcome.Failure);
    }

    [Theory]
    [InlineData(ProvisionedWifiStatus.Unavailable, ProvisionedWifiUnavailableReason.SecretUnavailable, NetworkFailureCode.WifiSecretUnavailable)]
    [InlineData(ProvisionedWifiStatus.Unavailable, ProvisionedWifiUnavailableReason.ProfileTemplateMissing, NetworkFailureCode.ProvisionedProfileUnavailable)]
    [InlineData(ProvisionedWifiStatus.NotConfigured, ProvisionedWifiUnavailableReason.None, NetworkFailureCode.ProvisionedProfileUnavailable)]
    public async Task ConnectProvisionedWifi_WhenProfileIsNotReady_ReturnsTypedReason(
        ProvisionedWifiStatus status, ProvisionedWifiUnavailableReason reason, NetworkFailureCode expected)
    {
        ProvisioningOutcome provisioning = new(Dot1xState.NotConfigured, new ProvisionedWifiState(status, reason), null, []);
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(provisioning);
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Lab"), Token);

        OperationOutcome outcome = await coordinator.ConnectProvisionedWifiAsync(Token);

        Assert.Equal(expected, outcome.Failure);
        Assert.Equal(ConnectionMode.Manual, coordinator.State.Mode);
    }

    [Fact]
    public async Task ConnectProvisionedWifi_SetsTheProfileOnlyWhenTheInterfaceLacksIt()
    {
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(ReadyLab());
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.Online, null, "Lab"), Token);

        await coordinator.ConnectProvisionedWifiAsync(Token);
        await coordinator.ConnectProvisionedWifiAsync(Token);

        Assert.Single(_wlan.SetProfileCalls);
        Assert.Equal(2, _wlan.ConnectCalls.Count);
    }

    [Fact]
    public async Task Failure_LogsStartOutcomeAndOneStructuredWarning()
    {
        _wlan.ConnectResults.Enqueue(new WlanOperationResult(WlanOperationStatus.ConnectionFailed, 0, 0x00038005));
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(ProvisioningOutcome.NotConfigured);
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Guest"), Token);
        _logger.Entries.Clear();

        await coordinator.ConnectWifiNetworkAsync(WifiSsid.FromText("Guest"), "guest passphrase", Token);

        RecordedLogEntry warning = Assert.Single(_logger.Entries, static entry => entry.Level == LogLevel.Warning);
        Assert.Equal("connect", warning.Properties["Workflow"]);
        Assert.Equal("wifi.selected_action", warning.Properties["NetworkOperation"]);
        Assert.Equal("network", warning.Properties["FailureKind"]);
        Assert.Equal("connect_failed", warning.Properties["FailureReason"]);
        Assert.Equal("wifi_connect_failed", warning.Properties["FailureCode"]);
        Assert.Equal(0x00038005u, warning.Properties["WlanReasonCode"]);
        Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<string>(warning.Properties["OperationId"])));
        RecordedLogEntry started = Assert.Single(_logger.Entries, static entry => entry.Message.StartsWith("Network operation started.", StringComparison.Ordinal));
        Assert.Equal("manual", started.Properties["Trigger"]);
        RecordedLogEntry finished = Assert.Single(_logger.Entries, static entry => entry.Message.StartsWith("Network operation finished.", StringComparison.Ordinal));
        Assert.Equal(OperationStatus.Failed, finished.Properties["Status"]);
        Assert.False(finished.Properties.ContainsKey("Ssid"));
        Assert.DoesNotContain(_logger.Entries, static entry => Mentions(entry, "Guest") || Mentions(entry, "guest passphrase"));
    }

    [Fact]
    public async Task ApplyProvisioning_LogsEachHandledFailureOnce()
    {
        ProvisioningOutcome provisioning = new(
            Dot1xState.NotConfigured,
            new ProvisionedWifiState(ProvisionedWifiStatus.Unavailable, ProvisionedWifiUnavailableReason.ProfileTemplateMissing),
            null,
            [NetworkFailureCode.WifiCertificateImportFailed, NetworkFailureCode.ProvisionedProfileUnavailable]);

        await CreateProvisionedAsync(provisioning);

        RecordedLogEntry[] warnings = _logger.Entries.Where(static entry => entry.Level == LogLevel.Warning).ToArray();
        Assert.Equal(["wifi_certificate_import_failed", "wifi_profile_unavailable"], warnings.Select(static entry => entry.Properties["FailureCode"]));
        Assert.All(warnings, static entry => Assert.Equal("network.apply_provisioned_settings", entry.Properties["NetworkOperation"]));
        RecordedLogEntry started = Assert.Single(_logger.Entries, static entry => entry.Message.StartsWith("Network operation started.", StringComparison.Ordinal));
        Assert.Equal("startup", started.Properties["Trigger"]);
    }

    [Fact]
    public async Task ApplyObservation_LogsTransitionsNotPolls()
    {
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(ProvisioningOutcome.NotConfigured);

        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Guest"), Token);
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Guest", "Other"), Token);
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.Online, null, "Guest"), Token);

        Assert.Equal(2, _logger.Entries.Count(static entry => entry.Message.StartsWith("Network state changed.", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task OperationLogs_NeverContainTheSsid()
    {
        const string Ssid = "Secret-Lab-SSID";
        var sink = new CapturingSink();
        using Logger serilog = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        using ILoggerFactory factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddSerilog(serilog, dispose: false));
        _wlan.ConnectResults.Enqueue(new WlanOperationResult(WlanOperationStatus.ConnectionFailed, 0, 0x00038005));
        using var coordinator = new ConnectionCoordinator(new FakeProvisioningStep(ProvisioningOutcome.NotConfigured), _wlan,
            new ConnectNetworkOptions(true, TimeSpan.FromSeconds(10)), new NetworkRefreshSignal(), _clock, factory.CreateLogger<ConnectionCoordinator>());
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.Online, Ssid, Ssid), Token);

        await coordinator.ConnectWifiNetworkAsync(WifiSsid.FromText(Ssid), "guest passphrase", Token);

        // The PostHog Logs stream mirrors the message, every property and the exception of each event.
        var context = new RemoteDiagnosticsContext("Foundry.Connect", "1.0.0", "Release", "winpe", "x64", "en-US", "session", "1.0.0");
        Assert.Contains(sink.Events, static item => item.Level == LogEventLevel.Warning);
        Assert.Contains(sink.Events, static item => item.MessageTemplate.Text.StartsWith("Network state changed.", StringComparison.Ordinal));
        Assert.All(sink.Events, item =>
        {
            Assert.DoesNotContain(Ssid, item.RenderMessage(), StringComparison.Ordinal);
            Assert.DoesNotContain(item.Properties.Values, value => value.ToString().Contains(Ssid, StringComparison.Ordinal));
            Assert.DoesNotContain(Ssid, item.Exception?.ToString() ?? string.Empty, StringComparison.Ordinal);
            RemoteDiagnosticRecord record = LogRecordFactory.Create(item, context);
            Assert.DoesNotContain(Ssid, record.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(Ssid, JsonSerializer.Serialize(record.Attributes), StringComparison.Ordinal);
            Assert.DoesNotContain(Ssid, JsonSerializer.Serialize(record.Exception), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task ManualOperation_RunsWlanCallsOffTheCallingThread()
    {
        var wlan = new ObservingWlanClient(_wlan);
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(ProvisioningOutcome.NotConfigured, wlan: wlan);
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Guest"), Token);
        CancellationToken token = Token;
        int callerThread = 0;
        Task<OperationOutcome>? manual = null;

        // A dedicated thread, unlike a pool thread, can never be the one that later runs the queued work.
        var caller = new Thread(() =>
        {
            callerThread = Environment.CurrentManagedThreadId;
            manual = coordinator.ConnectWifiNetworkAsync(WifiSsid.FromText("Guest"), "guest passphrase", token);
        });
        caller.Start();
        caller.Join();

        Assert.True((await manual!).IsSuccess);
        Assert.NotEmpty(wlan.CallThreads);
        Assert.DoesNotContain(callerThread, wlan.CallThreads);
    }

    [Fact]
    public async Task AutomaticBackoff_KeepsGrowingWhileAssociationNeverReachesInternet()
    {
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(ReadyLab());

        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Lab"), Token);
        Assert.True(coordinator.State.LastOutcome!.IsSuccess);
        _clock.Advance(TimeSpan.FromSeconds(19));
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Lab"), Token);
        Assert.Single(_wlan.ConnectCalls);

        _clock.Advance(TimeSpan.FromSeconds(1));
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Lab"), Token);
        Assert.Equal(2, _wlan.ConnectCalls.Count);

        _clock.Advance(TimeSpan.FromSeconds(20));
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Lab"), Token);
        Assert.Equal(2, _wlan.ConnectCalls.Count);

        _clock.Advance(TimeSpan.FromSeconds(20));
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Lab"), Token);
        Assert.Equal(3, _wlan.ConnectCalls.Count);
    }

    [Fact]
    public async Task AutomaticBackoff_ResetsOnceTheProvisionedConnectionIsOnline()
    {
        _wlan.ConnectResults.Enqueue(new WlanOperationResult(WlanOperationStatus.ConnectionFailed, 0, 0x00038005));
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(ReadyLab());
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Lab"), Token);
        _clock.Advance(TimeSpan.FromSeconds(20));
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Lab"), Token);
        Assert.Equal(2, _wlan.ConnectCalls.Count);

        await coordinator.ApplyObservationAsync(Observation(InternetStatus.Online, "Lab", "Lab"), Token);
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Lab"), Token);

        Assert.Equal(3, _wlan.ConnectCalls.Count);
    }

    [Fact]
    public async Task CancelledQueuedManualRequest_LeavesTheGateUsable()
    {
        _wlan.ConnectGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(ReadyLab());
        Task automatic = coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Lab", "Guest"), Token);
        await AsyncAssert.EventuallyAsync(() => _wlan.ConnectCalls.Count == 1, "the automatic attempt has started");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);

        Task<OperationOutcome> queued = coordinator.ConnectWifiNetworkAsync(WifiSsid.FromText("Guest"), "guest passphrase", cancellation.Token);
        await cancellation.CancelAsync();

        Assert.Equal(OperationStatus.Cancelled, (await queued).Status);
        _wlan.ConnectGate.SetResult();
        await automatic;
        Assert.True((await coordinator.ConnectWifiNetworkAsync(WifiSsid.FromText("Guest"), "guest passphrase", Token)).IsSuccess);
        Assert.Equal(["Lab", "Guest"], _wlan.ConnectCalls.Select(static call => call.ProfileName));
    }

    [Fact]
    public async Task UnexpectedException_IsFailedUnexpectedWithOneErrorAndNoWarning()
    {
        var wlan = new ObservingWlanClient(_wlan) { SetProfileException = new InvalidOperationException("boom") };
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(ProvisioningOutcome.NotConfigured, wlan: wlan);
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Guest"), Token);
        _logger.Entries.Clear();

        OperationOutcome outcome = await coordinator.ConnectWifiNetworkAsync(WifiSsid.FromText("Guest"), "guest passphrase", Token);

        Assert.Equal(OperationOutcome.Failed(NetworkOperationKind.ConnectWifiNetwork, NetworkFailureCode.Unexpected), outcome);
        RecordedLogEntry error = Assert.Single(_logger.Entries, static entry => entry.Level == LogLevel.Error);
        Assert.Null(error.Exception);
        Assert.Equal(nameof(InvalidOperationException), error.Properties["FailureType"]);
        Assert.Equal(new InvalidOperationException().HResult, error.Properties["ErrorCode"]);
        Assert.DoesNotContain(_logger.Entries, static entry => entry.Level == LogLevel.Warning);
        Assert.Null(coordinator.State.ActiveOperation);
    }

    [Fact]
    public async Task StateChanged_IsRaisedWithThePublishedState()
    {
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(ProvisioningOutcome.NotConfigured);
        var raised = new List<NetworkState>();
        coordinator.StateChanged += (_, state) => raised.Add(state);

        await coordinator.ApplyObservationAsync(Observation(InternetStatus.Online, null, "Guest"), Token);

        NetworkState published = Assert.Single(raised);
        Assert.Same(coordinator.State, published);
        Assert.Equal(InternetStatus.Online, published.Internet);
    }

    [Fact]
    public async Task ThrowingStateChangedSubscriber_DoesNotStopTheOperation()
    {
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(ProvisioningOutcome.NotConfigured);
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Guest"), Token);
        var later = new List<NetworkState>();
        coordinator.StateChanged += (_, _) => throw new InvalidOperationException("Secret-Lab-SSID");
        coordinator.StateChanged += (_, state) => later.Add(state);
        _logger.Entries.Clear();

        OperationOutcome outcome = await coordinator.ConnectWifiNetworkAsync(WifiSsid.FromText("Guest"), "guest passphrase", Token);

        Assert.True(outcome.IsSuccess);
        Assert.Null(coordinator.State.ActiveOperation);
        Assert.Equal(outcome, coordinator.State.LastOutcome);
        Assert.NotEmpty(later);
        Assert.Single(_logger.Entries, static entry => entry.Message.StartsWith("Network operation finished.", StringComparison.Ordinal));
        RecordedLogEntry warning = Assert.Single(
            _logger.Entries.Where(static entry => entry.Level == LogLevel.Warning).DistinctBy(static entry => entry.Message));
        Assert.Equal(typeof(InvalidOperationException).FullName, warning.Properties["ExceptionType"]);
        Assert.Null(warning.Exception);
        Assert.DoesNotContain(_logger.Entries, static entry => Mentions(entry, "Secret-Lab-SSID"));
    }

    [Fact]
    public async Task Operation_ReleasesTheGateWhenTheRefreshSignalIsDisposed()
    {
        var signal = new NetworkRefreshSignal();
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(ProvisioningOutcome.NotConfigured, signal: signal);
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Guest"), Token);

        // Consume the provisioning request first: a pending request would make Request return before touching the
        // disposed semaphore.
        await signal.WaitAsync(Token);
        signal.Dispose();

        OperationOutcome first = await coordinator.ConnectWifiNetworkAsync(WifiSsid.FromText("Guest"), "guest passphrase", Token);
        OperationOutcome second = await coordinator.DisconnectWifiAsync(Token);

        Assert.True(first.IsSuccess);
        Assert.Equal(OperationStatus.NoChange, second.Status);
    }

    [Fact]
    public async Task ReleaseProvisionedProfile_ClearsTheDocumentAndBlocksProvisionedConnects()
    {
        ProvisioningOutcome provisioning = ReadyLab();
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(provisioning);
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.Online, null, "Lab"), Token);

        coordinator.ReleaseProvisionedProfile();
        OperationOutcome outcome = await coordinator.ConnectProvisionedWifiAsync(Token);

        Assert.Throws<ObjectDisposedException>(() => provisioning.WifiProfile!.Xml);
        Assert.Equal(NetworkFailureCode.ProvisionedProfileUnavailable, outcome.Failure);
        Assert.Empty(_wlan.ConnectCalls);
    }

    [Theory]
    [InlineData(WlanOperationStatus.RuntimeMissing, NetworkFailureCode.WifiRuntimeMissing)]
    [InlineData(WlanOperationStatus.InterfaceNotFound, NetworkFailureCode.NoWirelessAdapter)]
    [InlineData(WlanOperationStatus.RequestFailed, NetworkFailureCode.WifiProfileRejected)]
    public async Task ConnectWifiNetwork_MapsSetProfileFailures(WlanOperationStatus status, NetworkFailureCode expected)
    {
        _wlan.SetProfileResult = new WlanOperationResult(status, 5);
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(ProvisioningOutcome.NotConfigured);
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Guest"), Token);

        OperationOutcome outcome = await coordinator.ConnectWifiNetworkAsync(WifiSsid.FromText("Guest"), "guest passphrase", Token);

        Assert.Equal(OperationOutcome.Failed(NetworkOperationKind.ConnectWifiNetwork, expected), outcome);
        Assert.Empty(_wlan.ConnectCalls);
    }

    [Theory]
    [InlineData(WlanOperationStatus.RequestFailed, NetworkFailureCode.DisconnectFailed)]
    [InlineData(WlanOperationStatus.TimedOut, NetworkFailureCode.DisconnectTimedOut)]
    [InlineData(WlanOperationStatus.RuntimeMissing, NetworkFailureCode.WifiRuntimeMissing)]
    [InlineData(WlanOperationStatus.InterfaceNotFound, NetworkFailureCode.NoWirelessAdapter)]
    public async Task DisconnectWifi_MapsFailures(WlanOperationStatus status, NetworkFailureCode expected)
    {
        _wlan.DisconnectResult = new WlanOperationResult(status);
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(ProvisioningOutcome.NotConfigured);
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.Online, "Guest", "Guest"), Token);

        OperationOutcome outcome = await coordinator.DisconnectWifiAsync(Token);

        Assert.Equal(OperationOutcome.Failed(NetworkOperationKind.DisconnectWifi, expected), outcome);
    }

    [Fact]
    public async Task ApplyProvisioning_DoesNotRepeatTheWarningOfUnexpectedStepFailures()
    {
        ProvisioningOutcome provisioning = new(
            Dot1xState.NotConfigured,
            ProvisionedWifiState.NotConfigured,
            null,
            [NetworkFailureCode.Unexpected, NetworkFailureCode.WifiCertificateImportFailed]);

        await CreateProvisionedAsync(provisioning);

        RecordedLogEntry warning = Assert.Single(_logger.Entries, static entry => entry.Level == LogLevel.Warning);
        Assert.Equal("wifi_certificate_import_failed", warning.Properties["FailureCode"]);
        RecordedLogEntry finished = Assert.Single(_logger.Entries, static entry => entry.Message.StartsWith("Network operation finished.", StringComparison.Ordinal));
        Assert.Equal(OperationStatus.Failed, finished.Properties["Status"]);
    }

    [Fact]
    public async Task ApplyProvisioning_WithOnlyUnexpectedStepFailures_LogsNoWarning()
    {
        ProvisioningOutcome provisioning = new(Dot1xState.NotConfigured, ProvisionedWifiState.NotConfigured, null, [NetworkFailureCode.Unexpected]);

        await CreateProvisionedAsync(provisioning);

        Assert.DoesNotContain(_logger.Entries, static entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task SuccessfulWifiConnections_RecordsManualAndProvisionedSuccessesOnly()
    {
        _wlan.ConnectResults.Enqueue(new WlanOperationResult(WlanOperationStatus.ConnectionFailed, 0, 0x00038005));
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(ReadyLab());
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.Online, null, "Lab", "Guest"), Token);

        await coordinator.ConnectWifiNetworkAsync(WifiSsid.FromText("Guest"), "guest passphrase", Token);
        Assert.Empty(coordinator.SuccessfulWifiConnections);

        await coordinator.ConnectProvisionedWifiAsync(Token);
        await coordinator.ConnectWifiNetworkAsync(WifiSsid.FromText("Guest"), "guest passphrase", Token);

        Assert.Equal(
            [
                new SuccessfulWifiConnection(WifiInterface, "Lab", WifiSsid.FromText("Lab"), WifiConnectionSource.Provisioned, _clock.GetUtcNow()),
                new SuccessfulWifiConnection(WifiInterface, "Guest", WifiSsid.FromText("Guest"), WifiConnectionSource.Manual, _clock.GetUtcNow())
            ],
            coordinator.SuccessfulWifiConnections);
    }

    [Fact]
    public async Task SuccessfulWifiConnections_RepeatedSuccessUpdatesTheTime()
    {
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(ProvisioningOutcome.NotConfigured);
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.Online, null, "Guest"), Token);
        await coordinator.ConnectWifiNetworkAsync(WifiSsid.FromText("Guest"), "guest passphrase", Token);
        DateTimeOffset first = _clock.GetUtcNow();

        _clock.Advance(TimeSpan.FromMinutes(3));
        await coordinator.ConnectWifiNetworkAsync(WifiSsid.FromText("Guest"), "guest passphrase", Token);

        SuccessfulWifiConnection entry = Assert.Single(coordinator.SuccessfulWifiConnections);
        Assert.Equal(first + TimeSpan.FromMinutes(3), entry.LastSucceededAt);
    }

    [Fact]
    public async Task ApplyObservation_RecordsAProvisionedConnectionWindowsJoinedByItself()
    {
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(ReadyLab());
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.Online, null, "Guest"), Token);
        await coordinator.ConnectWifiNetworkAsync(WifiSsid.FromText("Guest"), "guest passphrase", Token);
        _clock.Advance(TimeSpan.FromMinutes(1));

        await coordinator.ApplyObservationAsync(Observation(InternetStatus.Online, "Lab", "Lab", "Guest"), Token);
        DateTimeOffset seenAt = _clock.GetUtcNow();
        _clock.Advance(TimeSpan.FromMinutes(1));
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.Online, "Lab", "Lab", "Guest"), Token);

        Assert.Equal(
            new SuccessfulWifiConnection(WifiInterface, "Lab", WifiSsid.FromText("Lab"), WifiConnectionSource.Provisioned, seenAt),
            coordinator.SuccessfulWifiConnections[1]);
        RoamingSelection selection = RoamingSelectionPolicy.Select(
            coordinator.State,
            coordinator.SuccessfulWifiConnections,
            new ConnectNetworkProfileRoamingSettings
            {
                Wifi = new NetworkProfileRoamingTransportSettings { IsEnabled = true }
            },
            wiredDot1xEnabled: false);
        Assert.Equal(["Lab", "Guest"], selection.WifiProfiles.Select(item => item.ProfileName));
    }

    [Fact]
    public void SuccessfulWifiConnection_ToStringOmitsSsidAndProfileName()
    {
        var entry = new SuccessfulWifiConnection(WifiInterface, "Secret-Profile", WifiSsid.FromText("Secret-Lab-SSID"), WifiConnectionSource.Manual, _clock.GetUtcNow());

        string text = entry.ToString();

        Assert.DoesNotContain("Secret-Profile", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret-Lab-SSID", text, StringComparison.Ordinal);
        Assert.Contains("Manual", text, StringComparison.Ordinal);
    }

    private static ProvisioningOutcome LockedWifi(ProvisionedWifiUnavailableReason reason = ProvisionedWifiUnavailableReason.None) => new(Dot1xState.NotConfigured,
        new ProvisionedWifiState(ProvisionedWifiStatus.Locked, reason, WifiSsid.FromText("Lab")), null, []);

    [Fact]
    public async Task ApplyObservation_NoInternetAndLockedProvisionedWifi_RequestsUnlockOnce()
    {
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(LockedWifi());
        int requests = 0;
        coordinator.UnlockRequested += (_, _) => requests++;

        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Lab"), Token);
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Lab"), Token);

        Assert.Equal(1, requests);
        Assert.Empty(_wlan.ConnectCalls);
    }

    [Fact]
    public async Task ApplyObservation_EthernetOnline_NeverRequestsUnlock()
    {
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(LockedWifi());
        int requests = 0;
        coordinator.UnlockRequested += (_, _) => requests++;

        await coordinator.ApplyObservationAsync(Observation(InternetStatus.Online, null, "Lab"), Token);

        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task ApplyObservation_AfterACancelledUnlock_NeverRequestsUnlockAgain()
    {
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(LockedWifi(ProvisionedWifiUnavailableReason.UnlockCancelled));
        int requests = 0;
        coordinator.UnlockRequested += (_, _) => requests++;

        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Lab"), Token);

        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task ConnectProvisionedWifi_WhileLocked_FailsWithWifiSecretLocked()
    {
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(LockedWifi());

        OperationOutcome outcome = await coordinator.ConnectProvisionedWifiAsync(Token);

        Assert.Equal(NetworkFailureCode.WifiSecretLocked, outcome.Failure);
    }

    [Fact]
    public async Task RefreshProvisionedWifi_AfterUnlock_PublishesTheReadyProfileAndConnectsAutomatically()
    {
        var step = new FakeProvisioningStep(LockedWifi()) { Refreshed = ReadyLab() };
        ConnectionCoordinator coordinator = await CreateProvisionedAsync(step);

        ProvisionedWifiState refreshed = await coordinator.RefreshProvisionedWifiAsync(Token);
        await coordinator.ApplyObservationAsync(Observation(InternetStatus.NoInternet, null, "Lab"), Token);

        Assert.Equal(ProvisionedWifiStatus.Ready, refreshed.Status);
        Assert.Equal(1, step.RefreshCalls);
        Assert.Equal([(WifiInterface, "Lab")], _wlan.ConnectCalls);
        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("Trigger=manual", StringComparison.Ordinal)
            && entry.Message.Contains("network.apply_provisioned_settings", StringComparison.Ordinal));
    }

    private static bool Mentions(RecordedLogEntry entry, string text) =>
        entry.Message.Contains(text, StringComparison.Ordinal)
        || entry.Properties.Values.Any(value => value?.ToString()?.Contains(text, StringComparison.Ordinal) == true)
        || entry.Exception?.ToString().Contains(text, StringComparison.Ordinal) == true;

    private Task<ConnectionCoordinator> CreateProvisionedAsync(
        ProvisioningOutcome provisioning, bool wifiEnabled = true, IWlanClient? wlan = null, NetworkRefreshSignal? signal = null) =>
        CreateProvisionedAsync(new FakeProvisioningStep(provisioning), wifiEnabled, wlan, signal);

    private async Task<ConnectionCoordinator> CreateProvisionedAsync(
        FakeProvisioningStep step, bool wifiEnabled = true, IWlanClient? wlan = null, NetworkRefreshSignal? signal = null)
    {
        _wlan.Snapshot = new WlanSnapshot(WifiAvailability.Available, [FakeWlanClient.Interface(WifiInterface)], []);
        var coordinator = new ConnectionCoordinator(step, wlan ?? _wlan,
            new ConnectNetworkOptions(wifiEnabled, TimeSpan.FromSeconds(10)), signal ?? new NetworkRefreshSignal(), _clock, _logger);
        await coordinator.ApplyProvisioningAsync(Token);
        return coordinator;
    }

    private static ProvisioningOutcome ReadyLab(bool hidden = false) => new(
        Dot1xState.NotConfigured,
        new ProvisionedWifiState(ProvisionedWifiStatus.Ready, Ssid: WifiSsid.FromText("Lab"), ProfileName: "Lab", IsHidden: hidden),
        WifiProfileFactory.Build(WifiSsid.FromText("Lab"), WifiProfileSecurity.Wpa2Personal, "correct horse").Profile,
        []);

    private NetworkObservation Observation(InternetStatus internet, string? connectedSsid, params string[] visible) =>
        Observation(internet, connectedSsid, WifiSecurityType.Wpa2Personal, visible);

    private NetworkObservation Observation(InternetStatus internet, string? connectedSsid, WifiSecurityType security, params string[] visible) => new(
        [],
        new WifiObservation(
            WifiAvailability.Available,
            connectedSsid is null ? null : new WifiConnection(WifiInterface, WifiSsid.FromText(connectedSsid), connectedSsid, security),
            visible.Select(ssid => new VisibleWifiNetwork(WifiInterface, WifiSsid.FromText(ssid), security, 80)).ToArray(),
            [WifiInterface]),
        internet,
        null,
        _clock.GetUtcNow());

    // Returns its outcome; a refresh returns Refreshed when set, else the same outcome.
    private sealed class FakeProvisioningStep(ProvisioningOutcome outcome) : IProvisioningStep
    {
        public ProvisioningOutcome? Refreshed { get; init; }

        public int RefreshCalls { get; private set; }

        public Task<ProvisioningOutcome> RunAsync(CancellationToken cancellationToken) => Task.FromResult(outcome);

        public Task<ProvisioningOutcome> RefreshWifiAsync(CancellationToken cancellationToken)
        {
            RefreshCalls++;
            return Task.FromResult(Refreshed ?? outcome);
        }
    }

    // Records the thread of every WLAN call and can fail SetProfile with an exception.
    private sealed class ObservingWlanClient(FakeWlanClient inner) : IWlanClient
    {
        private readonly object _gate = new();
        private readonly List<int> _callThreads = [];

        public Exception? SetProfileException { get; init; }

        public IReadOnlyList<int> CallThreads
        {
            get
            {
                lock (_gate)
                {
                    return [.. _callThreads];
                }
            }
        }

        public WlanSnapshot GetSnapshot(bool requestScan) => Record(() => inner.GetSnapshot(requestScan));

        public WlanOperationResult SetProfile(Guid interfaceId, string profileXml) =>
            Record(() => SetProfileException is { } exception ? throw exception : inner.SetProfile(interfaceId, profileXml));

        public bool HasProfile(Guid interfaceId, string profileName) => Record(() => inner.HasProfile(interfaceId, profileName));

        public string? GetProfileXml(Guid interfaceId, string profileName) => Record(() => inner.GetProfileXml(interfaceId, profileName));

        public Task<WlanOperationResult> ConnectAsync(Guid interfaceId, string profileName, CancellationToken cancellationToken) =>
            Record(() => inner.ConnectAsync(interfaceId, profileName, cancellationToken));

        public Task<WlanOperationResult> DisconnectAsync(Guid interfaceId, CancellationToken cancellationToken) =>
            Record(() => inner.DisconnectAsync(interfaceId, cancellationToken));

        private T Record<T>(Func<T> call)
        {
            lock (_gate)
            {
                _callThreads.Add(Environment.CurrentManagedThreadId);
            }

            return call();
        }
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
