// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network.Native;
using Foundry.Connect.Network.Policy;
using Foundry.Utilities.Networking;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Network;

/// <summary>
/// Produces one <see cref="NetworkObservation"/> per call.
/// </summary>
public interface INetworkObservationSource
{
    /// <summary>
    /// Observes Ethernet, Wi-Fi, internet and, on request, wired 802.1X.
    /// </summary>
    /// <remarks>
    /// Each part is gathered independently: a part that fails is replaced by its degraded value and never hides the
    /// others (T11, NET-05/06). The returned internet status is never <see cref="InternetStatus.Checking"/>.
    /// </remarks>
    /// <param name="readWiredAuthentication">Whether to read the wired 802.1X authentication state.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The observation.</returns>
    /// <exception cref="OperationCanceledException">The caller cancelled; part failures never throw.</exception>
    Task<NetworkObservation> ObserveAsync(bool readWiredAuthentication, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="INetworkObservationSource"/> built from the adapter snapshot provider, the WLAN client, the connectivity
/// probe and the wired 802.1X client.
/// </summary>
/// <remarks>
/// The connectivity probe starts first so it runs while the synchronous Ethernet and WLAN reads happen, and it is awaited
/// last. Wireless discovery is flaky for a moment after a scan, so a non-empty network list is reused for
/// <see cref="WifiDiscoveryGracePeriod"/> when discovery briefly returns nothing. Part failures are logged on the
/// transition only (one Warning with the exception type, one Information on recovery); exception text can carry
/// identifying data, so the exception object is attached at Debug only. The network monitor runs the source off the
/// caller's thread, so the blocking Ethernet and WLAN reads never execute on the UI thread.
/// </remarks>
public sealed class NetworkObservationSource(
    INetworkAdapterSnapshotProvider adapters,
    IWlanClient wlan,
    IConnectivityProbe probe,
    IWiredDot1xClient wired,
    ConnectNetworkOptions options,
    TimeProvider timeProvider,
    ILogger<NetworkObservationSource> logger) : INetworkObservationSource
{
    /// <summary>
    /// Gets how long a non-empty Wi-Fi network list is reused when discovery briefly returns nothing (today's
    /// <c>NetworkStatusService</c> value).
    /// </summary>
    public static readonly TimeSpan WifiDiscoveryGracePeriod = TimeSpan.FromSeconds(15);

    private const string EthernetPart = "ethernet";
    private const string WifiPart = "wifi";
    private const string InternetPart = "internet";
    private const string WiredPart = "wired";

    private readonly INetworkAdapterSnapshotProvider _adapters = adapters ?? throw new ArgumentNullException(nameof(adapters));
    private readonly IWlanClient _wlan = wlan ?? throw new ArgumentNullException(nameof(wlan));
    private readonly IConnectivityProbe _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    private readonly IWiredDot1xClient _wired = wired ?? throw new ArgumentNullException(nameof(wired));
    private readonly ConnectNetworkOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILogger<NetworkObservationSource> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly Lock _stateLock = new();
    private readonly HashSet<string> _failedParts = [];

    private IReadOnlyList<VisibleWifiNetwork> _recentNetworks = [];
    private DateTimeOffset _recentNetworksAt;

    /// <inheritdoc />
    public async Task<NetworkObservation> ObserveAsync(bool readWiredAuthentication, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Task<InternetStatus> internetTask = ObserveInternetAsync(cancellationToken);
        IReadOnlyList<EthernetInterfaceState> ethernet = ObserveEthernet(cancellationToken);
        WifiObservation wifi = ObserveWifi(cancellationToken);
        Dot1xAuthenticationStatus? wiredAuthentication = readWiredAuthentication
            ? await ObserveWiredAsync(cancellationToken).ConfigureAwait(false)
            : null;
        InternetStatus internet = await internetTask.ConfigureAwait(false);

        return new NetworkObservation(ethernet, wifi, internet, wiredAuthentication, _timeProvider.GetUtcNow());
    }

    private async Task<InternetStatus> ObserveInternetAsync(CancellationToken cancellationToken)
    {
        try
        {
            InternetStatus status = await _probe.CheckAsync(cancellationToken).ConfigureAwait(false);
            ReportPartSucceeded(InternetPart);
            return status;
        }
        catch (Exception exception) when (IsPartFailure(exception, cancellationToken))
        {
            ReportPartFailed(InternetPart, exception);
            return InternetStatus.NoInternet;
        }
    }

    private IReadOnlyList<EthernetInterfaceState> ObserveEthernet(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<EthernetInterfaceState> ethernet = EthernetStateReader.Read(_adapters.GetAdapters());
            ReportPartSucceeded(EthernetPart);
            return ethernet;
        }
        catch (Exception exception) when (IsPartFailure(exception, cancellationToken))
        {
            ReportPartFailed(EthernetPart, exception);
            return [];
        }
    }

    private WifiObservation ObserveWifi(CancellationToken cancellationToken)
    {
        if (!_options.IsWifiFeatureEnabled)
        {
            return WifiObservation.Disabled;
        }

        WlanSnapshot snapshot;
        try
        {
            snapshot = _wlan.GetSnapshot(requestScan: true);
            ReportPartSucceeded(WifiPart);
        }
        catch (Exception exception) when (IsPartFailure(exception, cancellationToken))
        {
            ReportPartFailed(WifiPart, exception);
            return new WifiObservation(WifiAvailability.RuntimeMissing, null, [], []);
        }

        return new WifiObservation(
            snapshot.Availability,
            ReadConnection(snapshot),
            ResolveNetworks(snapshot),
            [.. snapshot.Interfaces.Select(static item => item.InterfaceId)]);
    }

    private async Task<Dot1xAuthenticationStatus?> ObserveWiredAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<WiredInterfaceAuthentication> interfaces = await _wired.ReadAuthenticationAsync(cancellationToken).ConfigureAwait(false);
            Dot1xAuthenticationStatus? status = NetshLanInterfaceParser.Aggregate(interfaces);
            ReportPartSucceeded(WiredPart);
            return status;
        }
        catch (Exception exception) when (IsPartFailure(exception, cancellationToken))
        {
            ReportPartFailed(WiredPart, exception);
            return null;
        }
    }

    private static WifiConnection? ReadConnection(WlanSnapshot snapshot)
    {
        foreach (WlanInterfaceSnapshot item in snapshot.Interfaces)
        {
            if (item.Status == WlanInterfaceStatus.Connected
                && item.Connection is { } connection
                && WifiSsid.TryFromBytes(connection.SsidBytes, out WifiSsid? ssid))
            {
                return new WifiConnection(
                    item.InterfaceId,
                    ssid,
                    connection.ProfileName,
                    WifiSecurityClassifier.Classify(connection.AuthAlgorithm, connection.SecurityEnabled));
            }
        }

        return null;
    }

    private IReadOnlyList<VisibleWifiNetwork> ResolveNetworks(WlanSnapshot snapshot)
    {
        IReadOnlyList<VisibleWifiNetwork> networks = WifiNetworkListPolicy.Build(snapshot.Networks);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        lock (_stateLock)
        {
            if (networks.Count > 0)
            {
                _recentNetworks = networks;
                _recentNetworksAt = now;
                return networks;
            }

            // Reuse only while the adapter is there; a missing adapter or runtime has no networks to keep.
            bool withinGrace = _recentNetworks.Count > 0 && now - _recentNetworksAt < WifiDiscoveryGracePeriod;
            return snapshot.Availability == WifiAvailability.Available && withinGrace ? _recentNetworks : networks;
        }
    }

    // Caller cancellation must reach the caller; any other exception, including a cancellation the part raised on its own, degrades the part.
    private static bool IsPartFailure(Exception exception, CancellationToken cancellationToken) =>
        !(exception is OperationCanceledException && cancellationToken.IsCancellationRequested);

    private void ReportPartFailed(string part, Exception exception)
    {
        bool transition;
        lock (_stateLock)
        {
            transition = _failedParts.Add(part);
        }

        if (!transition)
        {
            return;
        }

        _logger.LogWarning(
            "Network observation part failed. Part={Part}, FailureType={FailureType}",
            part,
            exception.GetType().Name);
        _logger.LogDebug(exception, "Network observation part failure detail. Part={Part}", part);
    }

    private void ReportPartSucceeded(string part)
    {
        bool recovered;
        lock (_stateLock)
        {
            recovered = _failedParts.Remove(part);
        }

        if (recovered)
        {
            _logger.LogInformation("Network observation part recovered. Part={Part}", part);
        }
    }
}
