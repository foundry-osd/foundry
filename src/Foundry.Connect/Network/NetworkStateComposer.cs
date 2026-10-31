// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Network;

/// <summary>
/// Builds the published <see cref="NetworkState"/> from the last observation and the coordinator's own data.
/// </summary>
public static class NetworkStateComposer
{
    /// <summary>
    /// Composes a network state.
    /// </summary>
    /// <remarks>
    /// The wired 802.1X state comes from provisioning; the observed authentication status replaces it only once the
    /// profile was applied, so an unavailable or rejected profile keeps its reason. The current Wi-Fi connection is
    /// marked <see cref="WifiConnectionSource.Provisioned"/> when <see cref="IsProvisionedConnection"/> matches.
    /// Without an observation the internet is <see cref="InternetStatus.Checking"/> and Wi-Fi is
    /// <see cref="WifiAvailability.Disabled"/>.
    /// </remarks>
    /// <param name="observation">Last monitor observation, or <see langword="null"/> before the first one.</param>
    /// <param name="mode">Current connection mode.</param>
    /// <param name="provisioning">Provisioning outcome, or <see langword="null"/> before provisioning ran.</param>
    /// <param name="activeOperation">Operation in flight, or <see langword="null"/>.</param>
    /// <param name="lastOutcome">Outcome of the last finished operation, or <see langword="null"/>.</param>
    /// <param name="now">Time used as <see cref="NetworkState.ObservedAt"/> when there is no observation.</param>
    /// <returns>The composed state.</returns>
    public static NetworkState Compose(
        NetworkObservation? observation,
        ConnectionMode mode,
        ProvisioningOutcome? provisioning,
        NetworkOperationKind? activeOperation,
        OperationOutcome? lastOutcome,
        DateTimeOffset now)
    {
        Dot1xState dot1x = MergeDot1x(provisioning?.Wired ?? Dot1xState.NotConfigured, observation?.WiredAuthentication);
        ProvisionedWifiState provisioned = provisioning?.Wifi ?? ProvisionedWifiState.NotConfigured;
        WifiObservation wifi = observation?.Wifi ?? WifiObservation.Disabled;

        WifiConnection? connection = wifi.Connection;
        if (connection is not null)
        {
            connection = connection with
            {
                Source = IsProvisionedConnection(connection, provisioned) ? WifiConnectionSource.Provisioned : WifiConnectionSource.Manual
            };
        }

        return new NetworkState(
            new EthernetState(observation?.Ethernet ?? [], dot1x),
            new WifiState(wifi.Availability, connection, wifi.Networks, provisioned),
            observation?.Internet ?? InternetStatus.Checking,
            mode,
            activeOperation,
            lastOutcome,
            observation?.ObservedAt ?? now);
    }

    /// <summary>
    /// Gets whether a connection uses the provisioned profile: the profile is ready and either the SSID bytes or the
    /// profile name (ordinal) match.
    /// </summary>
    /// <param name="connection">Current Wi-Fi connection.</param>
    /// <param name="provisioned">Provisioned profile state.</param>
    /// <returns><see langword="true"/> when the connection is the provisioned one.</returns>
    public static bool IsProvisionedConnection(WifiConnection connection, ProvisionedWifiState provisioned)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(provisioned);

        if (provisioned.Status != ProvisionedWifiStatus.Ready)
        {
            return false;
        }

        return (provisioned.Ssid is not null && connection.Ssid == provisioned.Ssid)
            || (provisioned.ProfileName is not null && string.Equals(connection.ProfileName, provisioned.ProfileName, StringComparison.Ordinal));
    }

    private static Dot1xState MergeDot1x(Dot1xState provisioned, Dot1xAuthenticationStatus? observed)
    {
        if (!provisioned.IsProfileApplied || observed is not Dot1xAuthenticationStatus status ||
            status is not (Dot1xAuthenticationStatus.ProfileApplied or Dot1xAuthenticationStatus.Authenticating or Dot1xAuthenticationStatus.Authenticated or Dot1xAuthenticationStatus.Failed))
        {
            return provisioned;
        }

        return new Dot1xState(status, status == Dot1xAuthenticationStatus.Failed ? Dot1xFailureReason.AuthenticationFailed : Dot1xFailureReason.None);
    }
}
