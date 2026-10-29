// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Network;

/// <summary>
/// What one monitor pass saw on the wireless side.
/// </summary>
/// <param name="Availability">Whether the Wi-Fi feature, the WLAN runtime and an adapter are available.</param>
/// <param name="Connection">Current Wi-Fi connection, or <see langword="null"/> when none.</param>
/// <param name="Networks">Visible networks, deduplicated by SSID.</param>
/// <param name="InterfaceIds">WLAN interface identifiers seen, used to apply the provisioned profile per interface.</param>
public sealed record WifiObservation(
    WifiAvailability Availability,
    WifiConnection? Connection,
    IReadOnlyList<VisibleWifiNetwork> Networks,
    IReadOnlyList<Guid> InterfaceIds)
{
    /// <summary>
    /// Gets the observation reported while the Wi-Fi feature is disabled.
    /// </summary>
    public static WifiObservation Disabled { get; } = new(WifiAvailability.Disabled, null, [], []);
}

/// <summary>
/// What one monitor pass saw. The connection coordinator composes it with its own mode and operation data into a
/// <see cref="NetworkState"/>.
/// </summary>
/// <param name="Ethernet">Ethernet interfaces seen.</param>
/// <param name="Wifi">Wireless observation.</param>
/// <param name="Internet">Internet reachability.</param>
/// <param name="WiredAuthentication">Wired 802.1X status, or <see langword="null"/> when not read or unreadable.</param>
/// <param name="ObservedAt">Time of the observation.</param>
public sealed record NetworkObservation(
    IReadOnlyList<EthernetInterfaceState> Ethernet,
    WifiObservation Wifi,
    InternetStatus Internet,
    Dot1xAuthenticationStatus? WiredAuthentication,
    DateTimeOffset ObservedAt);
