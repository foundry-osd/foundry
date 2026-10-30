// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;

namespace Foundry.Connect.ViewModels;

/// <summary>
/// Overall state of the single Connect page.
/// </summary>
public enum ConnectPageStatus
{
    /// <summary>The first observation has not completed yet, or the internet is re-checked right after a connection.</summary>
    Checking,

    /// <summary>A network operation runs, or wired 802.1X authenticates, and the internet is not online.</summary>
    Waiting,

    /// <summary>The internet is online; the technician can continue.</summary>
    Ready,

    /// <summary>The internet is not reachable; see <see cref="BlockedReason"/>.</summary>
    Blocked
}

/// <summary>
/// Closest fixable cause shown while the page is <see cref="ConnectPageStatus.Blocked"/>.
/// </summary>
public enum BlockedReason
{
    /// <summary>The page is not blocked.</summary>
    None,

    /// <summary>No Ethernet or usable Wi-Fi adapter.</summary>
    NoAdapter,

    /// <summary>An adapter exists but nothing is connected.</summary>
    NoLink,

    /// <summary>The Ethernet link is up but no usable address was obtained.</summary>
    NoIpAddress,

    /// <summary>The Ethernet link is up, wired 802.1X failed or is unavailable, and no usable address was obtained.</summary>
    WiredAuthentication,

    /// <summary>The network intercepts web traffic, typically a guest portal that needs a sign-in.</summary>
    CaptivePortal,

    /// <summary>A usable connection exists but the internet is not reachable.</summary>
    NoInternet
}

/// <summary>
/// Control that receives the keyboard focus after a page status change.
/// </summary>
public enum ConnectFocusTarget
{
    /// <summary>Focus does not move.</summary>
    None,

    /// <summary>The Continue button.</summary>
    Continue,

    /// <summary>The Retry button.</summary>
    Retry,

    /// <summary>The provisioned Wi-Fi Connect button.</summary>
    ProvisionedConnect,

    /// <summary>The visible Wi-Fi network list.</summary>
    WifiNetworks
}

/// <summary>
/// Page-level decisions over the typed network state: the page status, the blocked reason and where the
/// focus goes. Pure functions: they read the state and never change it.
/// </summary>
public static class ConnectPagePolicy
{
    /// <summary>
    /// Interval between two internet re-checks right after a connection (<see cref="StartsInternetRecheck"/>).
    /// </summary>
    public static readonly TimeSpan InternetRecheckInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long the internet is re-checked right after a connection before the page shows it blocked.
    /// </summary>
    public static readonly TimeSpan InternetRecheckDuration = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets whether this media offers Wi-Fi at all: the WLAN stack runs, with or without an adapter. The page decides
    /// its layout from this once, at the first observation.
    /// </summary>
    /// <param name="availability">The Wi-Fi availability.</param>
    /// <returns><see langword="true"/> for <see cref="WifiAvailability.Available"/> and <see cref="WifiAvailability.NoAdapter"/>.</returns>
    public static bool IsWifiOffered(WifiAvailability availability) =>
        availability is WifiAvailability.Available or WifiAvailability.NoAdapter;

    /// <summary>
    /// Gets whether the Wi-Fi section is shown: always in a two-column session, otherwise only while Wi-Fi works.
    /// </summary>
    /// <param name="usesTwoColumns">Whether the session layout has two columns.</param>
    /// <param name="availability">The current Wi-Fi availability.</param>
    /// <returns><see langword="true"/> when the Wi-Fi section is shown.</returns>
    public static bool IsWifiSectionShown(bool usesTwoColumns, WifiAvailability availability) =>
        usesTwoColumns || availability == WifiAvailability.Available;

    /// <summary>
    /// Gets whether wired 802.1X is still authenticating and the page should wait for it: the Ethernet link is up, no
    /// usable Ethernet address exists yet and no Wi-Fi connection is in use. There is no time limit.
    /// </summary>
    /// <param name="state">The network state.</param>
    /// <returns><see langword="true"/> while wired 802.1X authentication is pending.</returns>
    public static bool IsWiredAuthenticationPending(NetworkState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Ethernet.Dot1x.Status == Dot1xAuthenticationStatus.Authenticating
            && state.Ethernet.HasLink
            && !state.Ethernet.HasUsableAddress
            && state.Wifi.Connection is null;
    }

    /// <summary>
    /// Resolves the page status: ready when the internet is online, else waiting while an operation runs, else
    /// checking while the internet check is pending, else waiting while wired 802.1X authenticates
    /// (<see cref="IsWiredAuthenticationPending"/>), else checking during the re-check that follows a connection while the
    /// internet has not answered (<see cref="StartsInternetRecheck"/>), else blocked. A captive portal is an answer, so it
    /// stays blocked during the re-check.
    /// </summary>
    /// <param name="state">The network state.</param>
    /// <param name="isInternetRecheckPending">Whether the re-check that follows a connection is still running.</param>
    /// <returns>The page status.</returns>
    public static ConnectPageStatus ResolveStatus(NetworkState state, bool isInternetRecheckPending = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Internet == InternetStatus.Online)
        {
            return ConnectPageStatus.Ready;
        }

        if (state.ActiveOperation is not null)
        {
            return ConnectPageStatus.Waiting;
        }

        if (state.Internet == InternetStatus.Checking)
        {
            return ConnectPageStatus.Checking;
        }

        if (IsWiredAuthenticationPending(state))
        {
            return ConnectPageStatus.Waiting;
        }

        return isInternetRecheckPending && state.Internet == InternetStatus.NoInternet ? ConnectPageStatus.Checking : ConnectPageStatus.Blocked;
    }

    /// <summary>
    /// Gets whether a connection was just made, so the internet is re-checked for a short while before the page shows it
    /// blocked: a Wi-Fi connect (manual or provisioned, by the technician or automatic) has just succeeded, or, after an
    /// earlier observation, the Ethernet link came up or a usable Ethernet address appeared. Nothing starts once the
    /// internet is online.
    /// </summary>
    /// <remarks>
    /// The coordinator creates one outcome per finished operation and republishes that same instance with every later
    /// observation, so a new succeeded connect outcome is recognized by reference, even when it equals the previous one.
    /// </remarks>
    /// <param name="previous">The previously received live state.</param>
    /// <param name="current">The newly received live state.</param>
    /// <returns><see langword="true"/> when the re-check should (re)start.</returns>
    public static bool StartsInternetRecheck(NetworkState previous, NetworkState current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        if (current.Internet == InternetStatus.Online)
        {
            return false;
        }

        bool connected = !ReferenceEquals(previous.LastOutcome, current.LastOutcome)
            && current.LastOutcome is { Status: OperationStatus.Succeeded, Operation: NetworkOperationKind.ConnectWifiNetwork or NetworkOperationKind.ConnectProvisionedWifi };
        bool ethernetCameUp = previous.Internet != InternetStatus.Checking
            && ((!previous.Ethernet.HasLink && current.Ethernet.HasLink)
                || (!previous.Ethernet.HasUsableAddress && current.Ethernet.HasUsableAddress));
        return connected || ethernetCameUp;
    }

    /// <summary>
    /// Resolves why the page is blocked, naming the closest fixable cause: a captive portal, then no internet behind a
    /// usable connection, then a failed or unavailable wired 802.1X behind an Ethernet link, then no address behind an
    /// Ethernet link, then no link behind an adapter, then no adapter.
    /// </summary>
    /// <param name="state">The network state.</param>
    /// <param name="isInternetRecheckPending">Whether the re-check that follows a connection is still running.</param>
    /// <returns>The reason, or <see cref="BlockedReason.None"/> when the page is not blocked.</returns>
    public static BlockedReason ResolveBlockedReason(NetworkState state, bool isInternetRecheckPending = false)
    {
        if (ResolveStatus(state, isInternetRecheckPending) != ConnectPageStatus.Blocked)
        {
            return BlockedReason.None;
        }

        if (state.Internet == InternetStatus.CaptivePortal)
        {
            return BlockedReason.CaptivePortal;
        }

        if (state.Ethernet.HasUsableAddress || state.Wifi.Connection is not null)
        {
            return BlockedReason.NoInternet;
        }

        if (state.Ethernet.HasLink)
        {
            return state.Ethernet.Dot1x.Status is Dot1xAuthenticationStatus.Failed or Dot1xAuthenticationStatus.Unavailable
                ? BlockedReason.WiredAuthentication
                : BlockedReason.NoIpAddress;
        }

        return state.Ethernet.HasAdapter || state.Wifi.Availability == WifiAvailability.Available
            ? BlockedReason.NoLink
            : BlockedReason.NoAdapter;
    }

    /// <summary>
    /// Chooses the control that takes the focus when the page enters <paramref name="status"/>. Focus moves only for
    /// <see cref="ConnectPageStatus.Ready"/> and <see cref="ConnectPageStatus.Blocked"/>, and never once the technician
    /// interacted with networking this session, whatever the new status: a technician who is typing or browsing the
    /// Wi-Fi list keeps the focus.
    /// </summary>
    /// <param name="status">The new page status.</param>
    /// <param name="hasNetworkingInteraction">Whether the technician already interacted with networking.</param>
    /// <param name="canConnectProvisioned">Whether the provisioned Wi-Fi can be connected now.</param>
    /// <param name="hasVisibleNetworks">Whether the Wi-Fi list has networks.</param>
    /// <returns>The focus target.</returns>
    public static ConnectFocusTarget ResolveFocus(ConnectPageStatus status, bool hasNetworkingInteraction, bool canConnectProvisioned, bool hasVisibleNetworks) => status switch
    {
        _ when hasNetworkingInteraction => ConnectFocusTarget.None,
        ConnectPageStatus.Ready => ConnectFocusTarget.Continue,
        ConnectPageStatus.Blocked when canConnectProvisioned => ConnectFocusTarget.ProvisionedConnect,
        ConnectPageStatus.Blocked when hasVisibleNetworks => ConnectFocusTarget.WifiNetworks,
        ConnectPageStatus.Blocked => ConnectFocusTarget.Retry,
        _ => ConnectFocusTarget.None
    };
}
