// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Network.Native;

/// <summary>
/// Authentication algorithm reported by wlanapi (<c>DOT11_AUTH_ALGORITHM</c>, <c>wlantypes.h</c>).
/// </summary>
public enum WlanAuthAlgorithm : uint
{
    /// <summary>IEEE 802.11 Open System.</summary>
    Open80211 = 1,

    /// <summary>IEEE 802.11 Shared Key (WEP).</summary>
    SharedKey = 2,

    /// <summary>WPA with 802.1X.</summary>
    Wpa = 3,

    /// <summary>WPA with a pre-shared key.</summary>
    WpaPsk = 4,

    /// <summary>WPA without authentication (ad hoc).</summary>
    WpaNone = 5,

    /// <summary>WPA2 (RSNA) with 802.1X.</summary>
    Rsna = 6,

    /// <summary>WPA2 (RSNA) with a pre-shared key.</summary>
    RsnaPsk = 7,

    /// <summary>WPA3-Enterprise 192-bit mode (<c>DOT11_AUTH_ALGO_WPA3</c> / <c>WPA3_ENT_192</c>).</summary>
    Wpa3Enterprise192 = 8,

    /// <summary>WPA3-Personal (SAE).</summary>
    Wpa3Sae = 9,

    /// <summary>Opportunistic Wireless Encryption (Enhanced Open).</summary>
    Owe = 10,

    /// <summary>WPA3-Enterprise.</summary>
    Wpa3Enterprise = 11,

    /// <summary>First vendor-specific value.</summary>
    IhvStart = 0x80000000,

    /// <summary>Last vendor-specific value.</summary>
    IhvEnd = 0xffffffff
}

/// <summary>
/// Interface state grouped for Connect: every transitional association state is <see cref="Connecting"/>.
/// </summary>
public enum WlanInterfaceStatus
{
    /// <summary>The interface is not ready to operate.</summary>
    NotReady,

    /// <summary>The interface is connected (infrastructure or a formed ad hoc network).</summary>
    Connected,

    /// <summary>The interface is discovering, associating or authenticating.</summary>
    Connecting,

    /// <summary>The interface is disconnecting.</summary>
    Disconnecting,

    /// <summary>The interface is not connected.</summary>
    Disconnected
}

/// <summary>
/// Typed status of a WLAN operation.
/// </summary>
public enum WlanOperationStatus
{
    /// <summary>The operation completed.</summary>
    Succeeded,

    /// <summary><c>wlanapi.dll</c> is absent or the WLAN service does not answer.</summary>
    RuntimeMissing,

    /// <summary>The interface no longer exists.</summary>
    InterfaceNotFound,

    /// <summary>wlanapi refused the profile XML (<c>ERROR_BAD_PROFILE</c>); the reason code tells why.</summary>
    ProfileRejected,

    /// <summary>The call itself failed; <see cref="WlanOperationResult.Win32Error"/> holds the error.</summary>
    RequestFailed,

    /// <summary>The connection attempt ended with a WLAN reason code.</summary>
    ConnectionFailed,

    /// <summary>No terminal result arrived before the operation timeout.</summary>
    TimedOut
}

/// <summary>
/// Result of a WLAN operation; failures are values, never exceptions.
/// </summary>
/// <param name="Status">Typed status.</param>
/// <param name="Win32Error">Win32 error returned by wlanapi, or 0.</param>
/// <param name="ReasonCode">WLAN reason code (<c>WLAN_REASON_CODE</c>), or 0.</param>
public sealed record WlanOperationResult(WlanOperationStatus Status, uint Win32Error = 0, uint ReasonCode = 0)
{
    /// <summary>
    /// Gets a value indicating whether the operation succeeded.
    /// </summary>
    public bool IsSuccess => Status == WlanOperationStatus.Succeeded;
}

/// <summary>
/// Current association of a connected interface.
/// </summary>
/// <param name="SsidBytes">Raw SSID octets (not decoded, not trimmed).</param>
/// <param name="ProfileName">Name of the profile used for the connection.</param>
/// <param name="AuthAlgorithm">Authentication algorithm of the association.</param>
/// <param name="SecurityEnabled">Whether security is enabled on the association.</param>
public sealed record WlanCurrentConnection(byte[] SsidBytes, string ProfileName, WlanAuthAlgorithm AuthAlgorithm, bool SecurityEnabled);

/// <summary>
/// One wireless interface as read during a snapshot.
/// </summary>
/// <param name="InterfaceId">Interface GUID.</param>
/// <param name="Description">Adapter description.</param>
/// <param name="Status">Grouped interface state.</param>
/// <param name="Connection">Current association when <see cref="WlanInterfaceStatus.Connected"/>, otherwise <see langword="null"/>.</param>
public sealed record WlanInterfaceSnapshot(Guid InterfaceId, string Description, WlanInterfaceStatus Status, WlanCurrentConnection? Connection);

/// <summary>
/// One entry of an interface's available network list.
/// </summary>
/// <param name="InterfaceId">Interface that sees the network.</param>
/// <param name="SsidBytes">Raw SSID octets; empty for a hidden network.</param>
/// <param name="AuthAlgorithm">Default authentication algorithm.</param>
/// <param name="SecurityEnabled">Whether the network requires security.</param>
/// <param name="SignalQuality">Signal quality from 0 to 100.</param>
public sealed record WlanAvailableNetworkInfo(Guid InterfaceId, byte[] SsidBytes, WlanAuthAlgorithm AuthAlgorithm, bool SecurityEnabled, int SignalQuality);

/// <summary>
/// Raw WLAN view of the machine.
/// </summary>
/// <remarks>
/// <see cref="Availability"/> is <see cref="WifiAvailability.RuntimeMissing"/>, <see cref="WifiAvailability.NoAdapter"/>
/// or <see cref="WifiAvailability.Available"/>; never <see cref="WifiAvailability.Disabled"/>, which configuration decides.
/// </remarks>
/// <param name="Availability">Runtime and adapter availability.</param>
/// <param name="Interfaces">Wireless interfaces.</param>
/// <param name="Networks">Available networks of every interface, not merged, hidden ones included.</param>
/// <param name="RuntimeError">Win32 error that made the runtime unavailable, or 0.</param>
public sealed record WlanSnapshot(
    WifiAvailability Availability,
    IReadOnlyList<WlanInterfaceSnapshot> Interfaces,
    IReadOnlyList<WlanAvailableNetworkInfo> Networks,
    uint RuntimeError = 0)
{
    /// <summary>
    /// Creates the snapshot reported when the WLAN runtime cannot be used.
    /// </summary>
    /// <param name="error">Win32 error from wlanapi, or 0 when the library or an entry point is missing.</param>
    /// <returns>A snapshot without interfaces or networks.</returns>
    public static WlanSnapshot RuntimeMissing(uint error) => new(WifiAvailability.RuntimeMissing, [], [], error);
}

/// <summary>
/// Native WLAN client used by the network layer.
/// </summary>
/// <remarks>
/// No member throws for WLAN failures: errors become results (T11, NET-05/06). Caller cancellation still throws
/// <see cref="OperationCanceledException"/>. Each call opens and closes its own client handle.
/// </remarks>
public interface IWlanClient
{
    /// <summary>
    /// Reads interfaces, current connections and available networks.
    /// </summary>
    /// <param name="requestScan">Whether to ask each interface for a new scan after reading its list.</param>
    /// <returns>The snapshot; <see cref="WifiAvailability.RuntimeMissing"/> when wlanapi cannot be used.</returns>
    WlanSnapshot GetSnapshot(bool requestScan);

    /// <summary>
    /// Adds or overwrites an all-user profile on one interface.
    /// </summary>
    /// <param name="interfaceId">Interface GUID; WLAN profiles are per interface.</param>
    /// <param name="profileXml">Profile XML; never logged.</param>
    /// <returns>The result with the Win32 error and the WLAN reason code.</returns>
    WlanOperationResult SetProfile(Guid interfaceId, string profileXml);

    /// <summary>
    /// Returns whether a profile exists on an interface; <see langword="false"/> on any error.
    /// </summary>
    /// <param name="interfaceId">Interface GUID.</param>
    /// <param name="profileName">Profile name.</param>
    /// <returns><see langword="true"/> when wlanapi returns the profile.</returns>
    bool HasProfile(Guid interfaceId, string profileName);

    /// <summary>
    /// Reads a profile with <c>WLAN_PROFILE_GET_PLAINTEXT_KEY</c>.
    /// </summary>
    /// <remarks>
    /// The key comes back in plain text only for an administrator; otherwise it stays protected
    /// (<c>&lt;protected&gt;true&lt;/protected&gt;</c>) and the caller decides what to do.
    /// </remarks>
    /// <param name="interfaceId">Interface GUID.</param>
    /// <param name="profileName">Profile name.</param>
    /// <returns>The profile XML, or <see langword="null"/> on any error.</returns>
    string? GetProfileXml(Guid interfaceId, string profileName);

    /// <summary>
    /// Connects an interface with an existing profile and waits for the terminal ACM notification.
    /// </summary>
    /// <param name="interfaceId">Interface GUID.</param>
    /// <param name="profileName">Profile name.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The result with the WLAN reason code of a failed attempt.</returns>
    Task<WlanOperationResult> ConnectAsync(Guid interfaceId, string profileName, CancellationToken cancellationToken);

    /// <summary>
    /// Disconnects an interface and waits until it is no longer connected.
    /// </summary>
    /// <param name="interfaceId">Interface GUID.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The result.</returns>
    Task<WlanOperationResult> DisconnectAsync(Guid interfaceId, CancellationToken cancellationToken);
}
