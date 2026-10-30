// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network.Native;
using Foundry.Connect.Network.Policy;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Network;

/// <summary>
/// Serializes every network change behind one operation gate and publishes the composed <see cref="NetworkState"/>.
/// </summary>
/// <remarks>
/// Mode, provisioning outcome, last observation, operation data and the automatic-attempt backoff live behind one
/// lock; the gate (<see cref="SemaphoreSlim"/> with one slot) admits one operation at a time. A manual request
/// switches to <see cref="ConnectionMode.Manual"/> and publishes before it waits for the gate. An
/// automatic attempt never waits: it starts only when the gate is free, and the policy is evaluated while the gate is
/// held, so a manual request that arrived first always wins. Operation bodies run on the thread pool, never on the
/// caller's (UI) thread, because the WLAN calls block. Each operation releases the gate, then requests a refresh.
/// Every automatic attempt counts toward the backoff until an observation shows the provisioned connection online, so a
/// network that associates but never reaches the internet is retried ever more slowly. A provisioned Wi-Fi locked by the
/// deployment password never starts an attempt; the coordinator raises <see cref="UnlockRequested"/> once instead when the
/// attempt would otherwise run, and <see cref="RefreshProvisionedWifiAsync"/> builds the profile after the unlock.
/// </remarks>
public sealed class ConnectionCoordinator : IConnectionCoordinator, IWifiConnectionHistory
{
    private readonly object _stateLock = new();
    // Never disposed: an operation still running at shutdown releases it afterwards, and it owns no wait handle because
    // AvailableWaitHandle is never read.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IProvisioningStep _provisioningStep;
    private readonly IWlanClient _wlan;
    private readonly ConnectNetworkOptions _options;
    private readonly NetworkRefreshSignal _refreshSignal;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly NetworkOperationLogger _operationLogger;
    private readonly List<SuccessfulWifiConnection> _successfulConnections = [];

    private NetworkState _state;
    private ConnectionMode _mode = ConnectionMode.Automatic;
    private ProvisioningOutcome? _provisioning;
    private NetworkObservation? _lastObservation;
    private NetworkOperationKind? _activeOperation;
    private OperationOutcome? _lastOutcome;
    private int _consecutiveFailures;
    private DateTimeOffset? _lastAttemptAt;
    private bool _profileReleased;
    private bool _unlockRequested;

    /// <summary>
    /// Initializes the coordinator with the initial state (nothing observed, internet being checked, automatic mode).
    /// </summary>
    public ConnectionCoordinator(
        IProvisioningStep provisioning,
        IWlanClient wlan,
        ConnectNetworkOptions options,
        NetworkRefreshSignal refreshSignal,
        TimeProvider timeProvider,
        ILogger<ConnectionCoordinator> logger)
    {
        _provisioningStep = provisioning ?? throw new ArgumentNullException(nameof(provisioning));
        _wlan = wlan ?? throw new ArgumentNullException(nameof(wlan));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _refreshSignal = refreshSignal ?? throw new ArgumentNullException(nameof(refreshSignal));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _operationLogger = new NetworkOperationLogger(logger, timeProvider);

        // The provisioning publish (no observation yet) composes the same summary, so it logs no transition.
        _state = NetworkState.Initial(timeProvider.GetUtcNow());
    }

    /// <inheritdoc />
    public event EventHandler<NetworkState>? StateChanged;

    /// <inheritdoc />
    public event EventHandler? UnlockRequested;

    /// <inheritdoc />
    public NetworkState State => Volatile.Read(ref _state);

    /// <inheritdoc />
    public IReadOnlyList<SuccessfulWifiConnection> SuccessfulWifiConnections
    {
        get
        {
            lock (_stateLock)
            {
                return [.. _successfulConnections];
            }
        }
    }

    /// <inheritdoc />
    public async Task<ProvisioningOutcome> ApplyProvisioningAsync(CancellationToken cancellationToken)
    {
        ProvisioningOutcome? result = null;
        OperationOutcome outcome = await RunOperationAsync(
            NetworkOperationKind.ApplyProvisioning,
            NetworkOperationTrigger.Startup,
            async scope =>
            {
                ProvisioningOutcome provisioning = await _provisioningStep.RunAsync(cancellationToken).ConfigureAwait(false);
                lock (_stateLock)
                {
                    _provisioning = provisioning;
                }

                result = provisioning;
                return LogProvisioningFailures(scope, provisioning.Failures);
            },
            cancellationToken).ConfigureAwait(false);

        if (outcome.Status == OperationStatus.Cancelled)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        // An unexpected provisioning exception leaves nothing provisioned; Ethernet and manual Wi-Fi keep working.
        return result ?? ProvisioningOutcome.NotConfigured;
    }

    /// <inheritdoc />
    public async Task<ProvisionedWifiState> RefreshProvisionedWifiAsync(CancellationToken cancellationToken)
    {
        OperationOutcome outcome = await RunOperationAsync(
            NetworkOperationKind.ApplyProvisioning,
            NetworkOperationTrigger.Manual,
            async scope =>
            {
                ProvisioningOutcome refreshed = await _provisioningStep.RefreshWifiAsync(cancellationToken).ConfigureAwait(false);
                int previousFailureCount;
                lock (_stateLock)
                {
                    previousFailureCount = _provisioning?.Failures.Count ?? 0;
                    _provisioning = refreshed;

                    // The exit already dropped the profile document; a document built afterwards is not kept either.
                    if (_profileReleased)
                    {
                        refreshed.WifiProfile?.Clear();
                    }
                }

                // The merged outcome keeps the startup failures first; only those of the refresh are logged again.
                return LogProvisioningFailures(scope, refreshed.Failures.Skip(previousFailureCount).ToArray());
            },
            cancellationToken).ConfigureAwait(false);

        if (outcome.Status == OperationStatus.Cancelled)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return State.Wifi.Provisioned;
    }

    /// <inheritdoc />
    public async Task ApplyObservationAsync(NetworkObservation observation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observation);

        lock (_stateLock)
        {
            _lastObservation = observation;
        }

        WifiConnection? previousConnection = State.Wifi.Connection;
        NetworkState state = Publish();
        if (state.Wifi.Connection is { Source: WifiConnectionSource.Provisioned } provisioned)
        {
            RecordObservedProvisionedConnection(provisioned, previousConnection);
            if (state.Internet == InternetStatus.Online)
            {
                lock (_stateLock)
                {
                    _consecutiveFailures = 0;
                }
            }
        }

        // A locked profile never starts an automatic attempt, so the unlock request is decided here, after the publish.
        if (TryClaimUnlockRequest())
        {
            RaiseUnlockRequested();
        }

        // A running operation means no automatic attempt now (IsOperationRunning); never queue one behind it.
        if (!_gate.Wait(0))
        {
            return;
        }

        bool started = false;
        try
        {
            lock (_stateLock)
            {
                if (!ConnectionPolicy.ShouldConnectProvisionedAutomatically(BuildAutomaticInput()))
                {
                    return;
                }

                // Counted before the attempt: only an online provisioned connection resets the backoff, so an
                // association without internet keeps the delay growing.
                _lastAttemptAt = _timeProvider.GetUtcNow();
                _consecutiveFailures++;
            }

            started = true;
        }
        finally
        {
            if (!started)
            {
                _gate.Release();
            }
        }

        await RunUnderGateAsync(
            NetworkOperationKind.ConnectProvisionedWifi,
            NetworkOperationTrigger.Automatic,
            _ => ConnectProvisionedCoreAsync(cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<OperationOutcome> ConnectProvisionedWifiAsync(CancellationToken cancellationToken)
    {
        return RunManualAsync(
            NetworkOperationKind.ConnectProvisionedWifi,
            _ => ConnectProvisionedCoreAsync(cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationOutcome> ConnectWifiNetworkAsync(WifiSsid ssid, string? passphrase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ssid);

        return RunManualAsync(
            NetworkOperationKind.ConnectWifiNetwork,
            _ => ConnectWifiNetworkCoreAsync(ssid, passphrase, cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationOutcome> DisconnectWifiAsync(CancellationToken cancellationToken)
    {
        return RunManualAsync(
            NetworkOperationKind.DisconnectWifi,
            _ => DisconnectCoreAsync(cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public void ReleaseProvisionedProfile()
    {
        lock (_stateLock)
        {
            _provisioning?.WifiProfile?.Clear();
            _profileReleased = true;
        }
    }

    // The mode switch and its publish happen synchronously, before the returned task waits for the gate.
    private Task<OperationOutcome> RunManualAsync(
        NetworkOperationKind kind,
        Func<NetworkOperationScope, Task<OperationOutcome>> body,
        CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            _mode = ConnectionPolicy.AfterManualAction(_mode);
        }

        Publish();
        return RunOperationAsync(kind, NetworkOperationTrigger.Manual, body, cancellationToken);
    }

    private async Task<OperationOutcome> RunOperationAsync(
        NetworkOperationKind kind,
        NetworkOperationTrigger trigger,
        Func<NetworkOperationScope, Task<OperationOutcome>> body,
        CancellationToken cancellationToken)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancelled while queued: the operation never started, so nothing is logged.
            return OperationOutcome.Cancelled(kind);
        }

        return await RunUnderGateAsync(kind, trigger, body, cancellationToken).ConfigureAwait(false);
    }

    // Runs one operation while the caller holds the gate, and releases it.
    private async Task<OperationOutcome> RunUnderGateAsync(
        NetworkOperationKind kind,
        NetworkOperationTrigger trigger,
        Func<NetworkOperationScope, Task<OperationOutcome>> body,
        CancellationToken cancellationToken)
    {
        try
        {
            using NetworkOperationScope scope = _operationLogger.Begin(kind, trigger);
            lock (_stateLock)
            {
                _activeOperation = kind;
            }

            Publish();

            OperationOutcome outcome;
            try
            {
                // A free gate completes the wait synchronously, so without Task.Run the blocking WLAN calls would run
                // on the caller's (UI) thread. The logging scope flows with the execution context.
                outcome = await Task.Run(() => body(scope), CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                outcome = OperationOutcome.Cancelled(kind);
            }
            catch (Exception exception)
            {
                scope.LogException(exception);
                outcome = OperationOutcome.Failed(kind, NetworkFailureCode.Unexpected);
            }

            scope.LogOutcome(outcome);
            lock (_stateLock)
            {
                _lastOutcome = outcome;
                _activeOperation = null;
            }

            Publish();
            return outcome;
        }
        finally
        {
            _gate.Release();
            RequestRefresh();
        }
    }

    private void RequestRefresh()
    {
        try
        {
            _refreshSignal.Request();
        }
        catch (ObjectDisposedException)
        {
            // Shutdown disposed the monitor's signal; there is nothing left to refresh.
        }
    }

    private async Task<OperationOutcome> ConnectProvisionedCoreAsync(CancellationToken cancellationToken)
    {
        const NetworkOperationKind Kind = NetworkOperationKind.ConnectProvisionedWifi;
        if (!_options.IsWifiFeatureEnabled)
        {
            return OperationOutcome.Failed(Kind, NetworkFailureCode.WifiFeatureDisabled);
        }

        ProvisioningOutcome? provisioning;
        NetworkState state;
        IReadOnlyList<Guid> interfaceIds;
        bool released;
        lock (_stateLock)
        {
            provisioning = _provisioning;
            state = _state;
            interfaceIds = _lastObservation?.Wifi.InterfaceIds ?? [];
            released = _profileReleased;
        }

        ProvisionedWifiState provisioned = provisioning?.Wifi ?? ProvisionedWifiState.NotConfigured;
        if (provisioned.Status == ProvisionedWifiStatus.Locked)
        {
            return OperationOutcome.Failed(Kind, NetworkFailureCode.WifiSecretLocked);
        }

        if (provisioned.Status != ProvisionedWifiStatus.Ready)
        {
            return OperationOutcome.Failed(
                Kind,
                provisioned.Status == ProvisionedWifiStatus.Unavailable && provisioned.Reason == ProvisionedWifiUnavailableReason.SecretUnavailable
                    ? NetworkFailureCode.WifiSecretUnavailable
                    : NetworkFailureCode.ProvisionedProfileUnavailable);
        }

        WifiProfileDocument? profile = provisioning?.WifiProfile;
        if (profile is null || released)
        {
            return OperationOutcome.Failed(Kind, NetworkFailureCode.ProvisionedProfileUnavailable);
        }

        if (CheckAvailability(Kind, state.Wifi.Availability) is OperationOutcome unavailable)
        {
            return unavailable;
        }

        Guid? interfaceId = provisioned.Ssid is WifiSsid ssid
            ? state.Wifi.Networks.FirstOrDefault(network => network.Ssid == ssid)?.InterfaceId
            : null;
        interfaceId ??= interfaceIds.Count > 0 ? interfaceIds[0] : null;
        if (interfaceId is not Guid target)
        {
            return OperationOutcome.Failed(Kind, NetworkFailureCode.NoWirelessAdapter);
        }

        // WLAN profiles are per interface: an adapter that appeared after provisioning gets the profile now.
        if (!_wlan.HasProfile(target, profile.Name))
        {
            WlanOperationResult set = _wlan.SetProfile(target, profile.Xml);
            if (!set.IsSuccess)
            {
                return MapSetProfileFailure(Kind, set);
            }
        }

        WlanOperationResult result = await _wlan.ConnectAsync(target, profile.Name, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            RecordSuccessfulConnection(target, profile.Name, provisioned.Ssid ?? profile.Ssid, WifiConnectionSource.Provisioned);
        }

        return MapConnect(Kind, result);
    }

    private async Task<OperationOutcome> ConnectWifiNetworkCoreAsync(WifiSsid ssid, string? passphrase, CancellationToken cancellationToken)
    {
        const NetworkOperationKind Kind = NetworkOperationKind.ConnectWifiNetwork;
        if (!_options.IsWifiFeatureEnabled)
        {
            return OperationOutcome.Failed(Kind, NetworkFailureCode.WifiFeatureDisabled);
        }

        NetworkState state = State;
        if (CheckAvailability(Kind, state.Wifi.Availability) is OperationOutcome unavailable)
        {
            return unavailable;
        }

        VisibleWifiNetwork? network = state.Wifi.Networks.FirstOrDefault(item => item.Ssid == ssid);
        if (network is null)
        {
            return OperationOutcome.Failed(Kind, NetworkFailureCode.NetworkNotVisible);
        }

        if (WifiSecurityClassifier.ToProfileSecurity(network.Security) is not WifiProfileSecurity security)
        {
            return OperationOutcome.Failed(Kind, NetworkFailureCode.UnsupportedSecurity);
        }

        WifiProfileBuildResult build = WifiProfileFactory.Build(ssid, security, passphrase.AsSpan());
        if (build.Profile is not WifiProfileDocument document)
        {
            return OperationOutcome.Failed(Kind, NetworkFailureCode.InvalidPassphrase);
        }

        try
        {
            WlanOperationResult set = _wlan.SetProfile(network.InterfaceId, document.Xml);
            if (!set.IsSuccess)
            {
                return MapSetProfileFailure(Kind, set);
            }

            WlanOperationResult result = await _wlan.ConnectAsync(network.InterfaceId, document.Name, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                RecordSuccessfulConnection(network.InterfaceId, document.Name, ssid, WifiConnectionSource.Manual);
            }

            return MapConnect(Kind, result);
        }
        finally
        {
            // The manual passphrase is never kept.
            document.Clear();
        }
    }

    private async Task<OperationOutcome> DisconnectCoreAsync(CancellationToken cancellationToken)
    {
        const NetworkOperationKind Kind = NetworkOperationKind.DisconnectWifi;
        if (State.Wifi.Connection is not WifiConnection connection)
        {
            return OperationOutcome.NoChange(Kind);
        }

        WlanOperationResult result = await _wlan.DisconnectAsync(connection.InterfaceId, cancellationToken).ConfigureAwait(false);
        return result.Status switch
        {
            WlanOperationStatus.Succeeded => OperationOutcome.Success(Kind),
            WlanOperationStatus.RuntimeMissing => Failed(Kind, NetworkFailureCode.WifiRuntimeMissing, result),
            WlanOperationStatus.InterfaceNotFound => Failed(Kind, NetworkFailureCode.NoWirelessAdapter, result),
            WlanOperationStatus.TimedOut => Failed(Kind, NetworkFailureCode.DisconnectTimedOut, result),
            _ => Failed(Kind, NetworkFailureCode.DisconnectFailed, result)
        };
    }

    private static OperationOutcome? CheckAvailability(NetworkOperationKind kind, WifiAvailability availability)
    {
        return availability switch
        {
            WifiAvailability.Available => null,
            WifiAvailability.RuntimeMissing => OperationOutcome.Failed(kind, NetworkFailureCode.WifiRuntimeMissing),

            // Disabled here means no observation yet: no interface is known.
            _ => OperationOutcome.Failed(kind, NetworkFailureCode.NoWirelessAdapter)
        };
    }

    private static OperationOutcome MapConnect(NetworkOperationKind kind, WlanOperationResult result)
    {
        return result.Status switch
        {
            WlanOperationStatus.Succeeded => OperationOutcome.Success(kind),
            WlanOperationStatus.RuntimeMissing => Failed(kind, NetworkFailureCode.WifiRuntimeMissing, result),
            WlanOperationStatus.RequestFailed => Failed(kind, NetworkFailureCode.ConnectRequestFailed, result),
            WlanOperationStatus.ConnectionFailed => Failed(kind, NetworkFailureCode.ConnectionFailed, result),
            WlanOperationStatus.TimedOut => Failed(kind, NetworkFailureCode.ConnectTimedOut, result),
            WlanOperationStatus.ProfileRejected => Failed(kind, NetworkFailureCode.WifiProfileRejected, result),
            WlanOperationStatus.InterfaceNotFound => Failed(kind, NetworkFailureCode.NoWirelessAdapter, result),
            _ => Failed(kind, NetworkFailureCode.Unexpected, result)
        };
    }

    private static OperationOutcome MapSetProfileFailure(NetworkOperationKind kind, WlanOperationResult result)
    {
        return result.Status switch
        {
            WlanOperationStatus.RuntimeMissing => Failed(kind, NetworkFailureCode.WifiRuntimeMissing, result),
            WlanOperationStatus.InterfaceNotFound => Failed(kind, NetworkFailureCode.NoWirelessAdapter, result),
            _ => Failed(kind, NetworkFailureCode.WifiProfileRejected, result)
        };
    }

    private static OperationOutcome Failed(NetworkOperationKind kind, NetworkFailureCode failure, WlanOperationResult result)
    {
        return OperationOutcome.Failed(kind, failure, result.ReasonCode != 0 ? result.ReasonCode : null);
    }

    // One Warning per failure; the step logs its own Warning for an unexpected error.
    private static OperationOutcome LogProvisioningFailures(NetworkOperationScope scope, IReadOnlyList<NetworkFailureCode> failures)
    {
        foreach (NetworkFailureCode failure in failures)
        {
            if (failure == NetworkFailureCode.Unexpected)
            {
                scope.MarkFailureLogged();
            }
            else
            {
                scope.LogFailure(failure, null);
            }
        }

        return failures.Count == 0
            ? OperationOutcome.Success(NetworkOperationKind.ApplyProvisioning)
            : OperationOutcome.Failed(NetworkOperationKind.ApplyProvisioning, failures[0]);
    }

    // Decides and records the at-most-once unlock request under the state lock. A cancelled prompt counts as
    // already requested, so a technician who declined at the wired startup prompt is never prompted again.
    private bool TryClaimUnlockRequest()
    {
        lock (_stateLock)
        {
            bool alreadyRequested = _unlockRequested
                || _state.Wifi.Provisioned.Reason == ProvisionedWifiUnavailableReason.UnlockCancelled;
            AutomaticConnectInput input = BuildAutomaticInput() with { IsOperationRunning = _activeOperation is not null };
            if (!ConnectionPolicy.ShouldRequestUnlock(input, alreadyRequested))
            {
                return false;
            }

            _unlockRequested = true;
            return true;
        }
    }

    // Raised outside the lock; only the exception type of a failing handler is logged.
    private void RaiseUnlockRequested()
    {
        if (UnlockRequested is not { } handlers)
        {
            return;
        }

        foreach (EventHandler handler in handlers.GetInvocationList().Cast<EventHandler>())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                _logger.LogWarning("Unlock request subscriber failed. FailureType={FailureType}", exception.GetType().Name);
            }
        }
    }

    // Called with the state lock held. The automatic attempt also holds the gate, so it cannot overlap an operation.
    private AutomaticConnectInput BuildAutomaticInput()
    {
        WifiState wifi = _state.Wifi;
        return new AutomaticConnectInput(
            _mode,
            wifi.Provisioned.Status,
            wifi.Availability,
            ConnectionPolicy.IsProvisionedNetworkVisible(wifi),
            wifi.Provisioned.IsHidden,
            wifi.Connection?.Source == WifiConnectionSource.Provisioned,
            _state.Internet,
            IsOperationRunning: false,
            _consecutiveFailures,
            _lastAttemptAt,
            _timeProvider.GetUtcNow());
    }

    // One entry per interface and profile name (WLAN profiles are per interface); a repeated success updates it.
    private void RecordSuccessfulConnection(Guid interfaceId, string profileName, WifiSsid? ssid, WifiConnectionSource source)
    {
        var entry = new SuccessfulWifiConnection(interfaceId, profileName, ssid, source, _timeProvider.GetUtcNow());
        lock (_stateLock)
        {
            int index = _successfulConnections.FindIndex(item =>
                item.InterfaceId == interfaceId && string.Equals(item.ProfileName, profileName, StringComparison.Ordinal));
            if (index < 0)
            {
                _successfulConnections.Add(entry);
            }
            else
            {
                _successfulConnections[index] = entry;
            }
        }
    }

    // Windows joins an auto-mode provisioned profile by itself, so no Connect operation records that connection.
    // Connect provisioned the profile this session, so a connection seen for the first time, or again after another
    // state, counts as a success to roam. A connection that stays up is not re-recorded on every poll.
    private void RecordObservedProvisionedConnection(WifiConnection connection, WifiConnection? previous)
    {
        lock (_stateLock)
        {
            bool known = _successfulConnections.Exists(item =>
                item.InterfaceId == connection.InterfaceId && string.Equals(item.ProfileName, connection.ProfileName, StringComparison.Ordinal));
            if (known && Equals(previous, connection))
            {
                return;
            }

            RecordSuccessfulConnection(connection.InterfaceId, connection.ProfileName, connection.Ssid, WifiConnectionSource.Provisioned);
        }
    }

    private NetworkState Publish()
    {
        NetworkState current;
        lock (_stateLock)
        {
            NetworkState previous = _state;
            current = NetworkStateComposer.Compose(_lastObservation, _mode, _provisioning, _activeOperation, _lastOutcome, _timeProvider.GetUtcNow());
            Volatile.Write(ref _state, current);

            // Logged under the lock so transitions appear in publish order.
            _operationLogger.LogTransition(previous, current);
        }

        RaiseStateChanged(current);
        return current;
    }

    // A throwing subscriber must never leave ActiveOperation set or skip the terminal log, and must not stop the
    // other subscribers. Only the exception type is logged: subscriber messages may name a network.
    private void RaiseStateChanged(NetworkState state)
    {
        if (StateChanged is not { } handlers)
        {
            return;
        }

        foreach (EventHandler<NetworkState> handler in handlers.GetInvocationList().Cast<EventHandler<NetworkState>>())
        {
            try
            {
                handler(this, state);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    "Network state subscriber failed. ExceptionType={ExceptionType}",
                    exception.GetType().FullName);
            }
        }
    }
}
