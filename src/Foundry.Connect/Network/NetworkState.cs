// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Network;

/// <summary>
/// Internet reachability as seen by the connectivity probe.
/// </summary>
public enum InternetStatus
{
    /// <summary>No probe result yet.</summary>
    Checking,

    /// <summary>The probe reached the internet.</summary>
    Online,

    /// <summary>The network answered but intercepts web traffic, typically a guest portal that needs a sign-in.</summary>
    CaptivePortal,

    /// <summary>The probe received no answer.</summary>
    NoInternet
}

/// <summary>
/// Who decides automatic Wi-Fi connection attempts.
/// </summary>
public enum ConnectionMode
{
    /// <summary>Connect may start provisioned Wi-Fi attempts by itself.</summary>
    Automatic,

    /// <summary>The technician acted; Connect starts no further automatic attempt.</summary>
    Manual
}

/// <summary>
/// Network interface family that carries the internet traffic.
/// </summary>
public enum InternetPath
{
    /// <summary>No active internet path.</summary>
    None,

    /// <summary>A wired interface.</summary>
    Ethernet,

    /// <summary>A wireless interface.</summary>
    Wifi
}

/// <summary>
/// How an IPv4 address was obtained.
/// </summary>
public enum IpConfigurationKind
{
    /// <summary>No IPv4 address.</summary>
    None,

    /// <summary>Link-local address (169.254.0.0/16); it never reaches the internet.</summary>
    Apipa,

    /// <summary>Address leased from a DHCP server.</summary>
    Dhcp,

    /// <summary>Statically configured address.</summary>
    Static
}

/// <summary>
/// Wired 802.1X authentication status.
/// </summary>
public enum Dot1xAuthenticationStatus
{
    /// <summary>No wired 802.1X profile is configured.</summary>
    NotConfigured,

    /// <summary>A wired profile is configured but cannot be applied; see <see cref="Dot1xFailureReason"/>.</summary>
    Unavailable,

    /// <summary>The profile was applied; the authentication state is not known.</summary>
    ProfileApplied,

    /// <summary>Authentication is in progress.</summary>
    Authenticating,

    /// <summary>Authentication succeeded.</summary>
    Authenticated,

    /// <summary>Applying the profile or authenticating failed; see <see cref="Dot1xFailureReason"/>.</summary>
    Failed
}

/// <summary>
/// Reason attached to an unavailable or failed wired 802.1X state.
/// </summary>
public enum Dot1xFailureReason
{
    /// <summary>No failure.</summary>
    None,

    /// <summary>The wired secret could not be read.</summary>
    SecretUnavailable,

    /// <summary>The wired profile template is missing.</summary>
    ProfileTemplateMissing,

    /// <summary>Windows rejected the wired profile or its certificate.</summary>
    ProfileImportFailed,

    /// <summary>The profile is applied but authentication failed.</summary>
    AuthenticationFailed
}

/// <summary>
/// Whether Wi-Fi can be used on this machine.
/// </summary>
public enum WifiAvailability
{
    /// <summary>The Wi-Fi feature is not enabled for this media.</summary>
    Disabled,

    /// <summary>The WLAN runtime (service or <c>wlanapi.dll</c>) is not available.</summary>
    RuntimeMissing,

    /// <summary>The runtime is available but there is no wireless adapter.</summary>
    NoAdapter,

    /// <summary>At least one wireless adapter is usable.</summary>
    Available
}

/// <summary>
/// Security type of a Wi-Fi network, derived from the native authentication and cipher algorithms.
/// </summary>
public enum WifiSecurityType
{
    /// <summary>Unrecognized combination.</summary>
    Unknown,

    /// <summary>No authentication or encryption.</summary>
    Open,

    /// <summary>Opportunistic Wireless Encryption.</summary>
    Owe,

    /// <summary>WEP or shared key.</summary>
    Wep,

    /// <summary>WPA (version 1) with a pre-shared key.</summary>
    WpaPersonal,

    /// <summary>WPA2 with a pre-shared key.</summary>
    Wpa2Personal,

    /// <summary>WPA3 with SAE.</summary>
    Wpa3Personal,

    /// <summary>WPA (version 1) with 802.1X.</summary>
    WpaEnterprise,

    /// <summary>WPA2 with 802.1X.</summary>
    Wpa2Enterprise,

    /// <summary>WPA3 with 802.1X.</summary>
    Wpa3Enterprise
}

/// <summary>
/// Who created the current Wi-Fi connection profile.
/// </summary>
public enum WifiConnectionSource
{
    /// <summary>The technician connected through the network list.</summary>
    Manual,

    /// <summary>The profile came from the media configuration.</summary>
    Provisioned
}

/// <summary>
/// Readiness of the provisioned Wi-Fi profile.
/// </summary>
public enum ProvisionedWifiStatus
{
    /// <summary>The media carries no provisioned Wi-Fi profile.</summary>
    NotConfigured,

    /// <summary>A profile is configured but cannot be used; see <see cref="ProvisionedWifiUnavailableReason"/>.</summary>
    Unavailable,

    /// <summary>The profile can be applied and connected.</summary>
    Ready
}

/// <summary>
/// Why a configured provisioned Wi-Fi profile cannot be used.
/// </summary>
public enum ProvisionedWifiUnavailableReason
{
    /// <summary>The profile is usable.</summary>
    None,

    /// <summary>The Wi-Fi secret could not be read.</summary>
    SecretUnavailable,

    /// <summary>The enterprise profile template is missing.</summary>
    ProfileTemplateMissing,

    /// <summary>The configured fields are inconsistent.</summary>
    InvalidConfiguration,

    /// <summary>The passphrase does not satisfy the security type.</summary>
    InvalidPassphrase
}

/// <summary>
/// One Ethernet interface as seen by the monitor.
/// </summary>
/// <param name="Id">Stable interface identifier.</param>
/// <param name="Name">Interface display name.</param>
/// <param name="HasLink">Whether the cable is connected and the interface is operational.</param>
/// <param name="IpConfiguration">How the IPv4 address was obtained.</param>
/// <param name="Ipv4Address">IPv4 address, when any.</param>
/// <param name="Gateway">IPv4 default gateway, when any.</param>
public sealed record EthernetInterfaceState(
    string Id,
    string Name,
    bool HasLink,
    IpConfigurationKind IpConfiguration,
    string? Ipv4Address,
    string? Gateway)
{
    /// <summary>
    /// Gets a value indicating whether the interface has a link and a DHCP or static address. An APIPA address does
    /// not count.
    /// </summary>
    public bool HasUsableAddress => HasLink && IpConfiguration is IpConfigurationKind.Dhcp or IpConfigurationKind.Static;
}

/// <summary>
/// Wired 802.1X state.
/// </summary>
/// <param name="Status">Authentication status.</param>
/// <param name="Reason">Reason for an unavailable or failed status.</param>
public sealed record Dot1xState(Dot1xAuthenticationStatus Status, Dot1xFailureReason Reason = Dot1xFailureReason.None)
{
    /// <summary>
    /// Gets the state of a media without a wired 802.1X profile.
    /// </summary>
    public static Dot1xState NotConfigured { get; } = new(Dot1xAuthenticationStatus.NotConfigured);

    /// <summary>
    /// Gets a value indicating whether netsh accepted the wired profile. An authentication failure keeps the profile
    /// applied.
    /// </summary>
    public bool IsProfileApplied => Status is Dot1xAuthenticationStatus.ProfileApplied or Dot1xAuthenticationStatus.Authenticating or Dot1xAuthenticationStatus.Authenticated
        || (Status == Dot1xAuthenticationStatus.Failed && Reason == Dot1xFailureReason.AuthenticationFailed);
}

/// <summary>
/// Wired side of the network state.
/// </summary>
/// <param name="Interfaces">Ethernet interfaces seen.</param>
/// <param name="Dot1x">Wired 802.1X state.</param>
public sealed record EthernetState(IReadOnlyList<EthernetInterfaceState> Interfaces, Dot1xState Dot1x)
{
    /// <summary>
    /// Gets the state before the first observation.
    /// </summary>
    public static EthernetState Empty { get; } = new([], Dot1xState.NotConfigured);

    /// <summary>
    /// Gets a value indicating whether any Ethernet adapter exists.
    /// </summary>
    public bool HasAdapter => Interfaces.Count > 0;

    /// <summary>
    /// Gets a value indicating whether any interface has a link.
    /// </summary>
    public bool HasLink => Interfaces.Any(static item => item.HasLink);

    /// <summary>
    /// Gets a value indicating whether any interface has a usable address.
    /// </summary>
    public bool HasUsableAddress => Interfaces.Any(static item => item.HasUsableAddress);

    /// <summary>
    /// Gets the interface to describe: the first with a usable address, else the first with a link, else the first.
    /// </summary>
    public EthernetInterfaceState? Primary => Interfaces.FirstOrDefault(static item => item.HasUsableAddress)
        ?? Interfaces.FirstOrDefault(static item => item.HasLink)
        ?? Interfaces.FirstOrDefault();
}

/// <summary>
/// A Wi-Fi network in range.
/// </summary>
/// <param name="InterfaceId">WLAN interface that sees the network.</param>
/// <param name="Ssid">Network SSID.</param>
/// <param name="Security">Security type.</param>
/// <param name="SignalQuality">Signal quality from 0 to 100.</param>
public sealed record VisibleWifiNetwork(Guid InterfaceId, WifiSsid Ssid, WifiSecurityType Security, int SignalQuality);

/// <summary>
/// The current Wi-Fi connection.
/// </summary>
/// <param name="InterfaceId">WLAN interface that carries the connection.</param>
/// <param name="Ssid">Connected SSID.</param>
/// <param name="ProfileName">WLAN profile name; it may expose the SSID, so it must never be logged (the remote Logs stream mirrors every log property).</param>
/// <param name="Security">Security type.</param>
/// <param name="Source">Who created the profile.</param>
public sealed record WifiConnection(
    Guid InterfaceId,
    WifiSsid Ssid,
    string ProfileName,
    WifiSecurityType Security,
    WifiConnectionSource Source = WifiConnectionSource.Manual);

/// <summary>
/// Provisioned Wi-Fi profile state.
/// </summary>
/// <param name="Status">Readiness of the profile.</param>
/// <param name="Reason">Why the profile is unavailable.</param>
/// <param name="Ssid">Provisioned SSID, when configured.</param>
/// <param name="ProfileName">Provisioned WLAN profile name, when configured.</param>
/// <param name="IsEnterprise">Whether the profile uses 802.1X.</param>
/// <param name="IsHidden">Whether the profile targets a non-broadcast network. Such a network never appears in the visible list.</param>
public sealed record ProvisionedWifiState(
    ProvisionedWifiStatus Status,
    ProvisionedWifiUnavailableReason Reason = ProvisionedWifiUnavailableReason.None,
    WifiSsid? Ssid = null,
    string? ProfileName = null,
    bool IsEnterprise = false,
    bool IsHidden = false)
{
    /// <summary>
    /// Gets the state of a media without a provisioned Wi-Fi profile.
    /// </summary>
    public static ProvisionedWifiState NotConfigured { get; } = new(ProvisionedWifiStatus.NotConfigured);
}

/// <summary>
/// Wireless side of the network state.
/// </summary>
/// <param name="Availability">Whether Wi-Fi can be used.</param>
/// <param name="Connection">Current connection, or <see langword="null"/> when none.</param>
/// <param name="Networks">Visible networks.</param>
/// <param name="Provisioned">Provisioned profile state.</param>
public sealed record WifiState(
    WifiAvailability Availability,
    WifiConnection? Connection,
    IReadOnlyList<VisibleWifiNetwork> Networks,
    ProvisionedWifiState Provisioned)
{
    /// <summary>
    /// Gets the state before the first observation.
    /// </summary>
    public static WifiState Initial { get; } = new(WifiAvailability.Disabled, null, [], ProvisionedWifiState.NotConfigured);
}

/// <summary>
/// Immutable snapshot of the whole network situation, published by the connection coordinator.
/// </summary>
/// <param name="Ethernet">Wired state.</param>
/// <param name="Wifi">Wireless state.</param>
/// <param name="Internet">Internet reachability.</param>
/// <param name="Mode">Whether automatic Wi-Fi attempts are allowed.</param>
/// <param name="ActiveOperation">Operation in flight, or <see langword="null"/>.</param>
/// <param name="LastOutcome">Outcome of the last finished operation, or <see langword="null"/>.</param>
/// <param name="ObservedAt">Time of the observation behind this snapshot.</param>
public sealed record NetworkState(
    EthernetState Ethernet,
    WifiState Wifi,
    InternetStatus Internet,
    ConnectionMode Mode,
    NetworkOperationKind? ActiveOperation,
    OperationOutcome? LastOutcome,
    DateTimeOffset ObservedAt)
{
    /// <summary>
    /// Creates the state before the first observation: nothing known, internet being checked, automatic mode.
    /// </summary>
    /// <param name="now">Current time.</param>
    /// <returns>The initial state.</returns>
    public static NetworkState Initial(DateTimeOffset now) =>
        new(EthernetState.Empty, WifiState.Initial, InternetStatus.Checking, ConnectionMode.Automatic, null, null, now);

    /// <summary>
    /// Gets the interface family that carries the internet traffic. Windows prefers a wired route, so a usable
    /// Ethernet address makes Ethernet the active path; without internet there is no active path.
    /// </summary>
    public InternetPath ActiveInternetPath => Internet != InternetStatus.Online
        ? InternetPath.None
        : Ethernet.HasUsableAddress ? InternetPath.Ethernet
        : Wifi.Connection is not null ? InternetPath.Wifi
        : InternetPath.None;
}
