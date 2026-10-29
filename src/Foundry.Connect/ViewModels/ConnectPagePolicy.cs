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
    /// <summary>The first observation has not completed yet.</summary>
    Checking,

    /// <summary>A network operation runs and the internet is not online.</summary>
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
/// Page-level decisions over the typed network state (L3-2, L3-11): the page status, the blocked reason and where the
/// focus goes. Pure functions: they read the state and never change it.
/// </summary>
public static class ConnectPagePolicy
{
    /// <summary>
    /// Resolves the page status: ready when the internet is online, else waiting while an operation runs, else
    /// checking while the internet check is pending, else blocked.
    /// </summary>
    /// <param name="state">The network state.</param>
    /// <returns>The page status.</returns>
    public static ConnectPageStatus ResolveStatus(NetworkState state)
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

        return state.Internet == InternetStatus.Checking ? ConnectPageStatus.Checking : ConnectPageStatus.Blocked;
    }

    /// <summary>
    /// Resolves why the page is blocked, naming the closest fixable cause: a captive portal, then no internet behind a
    /// usable connection, then no address behind an Ethernet link, then no link behind an adapter, then no adapter.
    /// </summary>
    /// <param name="state">The network state.</param>
    /// <returns>The reason, or <see cref="BlockedReason.None"/> when the page is not blocked.</returns>
    public static BlockedReason ResolveBlockedReason(NetworkState state)
    {
        if (ResolveStatus(state) != ConnectPageStatus.Blocked)
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
            return BlockedReason.NoIpAddress;
        }

        return state.Ethernet.HasAdapter || state.Wifi.Availability == WifiAvailability.Available
            ? BlockedReason.NoLink
            : BlockedReason.NoAdapter;
    }

    /// <summary>
    /// Chooses the control that takes the focus when the page enters <paramref name="status"/>. Focus moves only for
    /// <see cref="ConnectPageStatus.Ready"/> and <see cref="ConnectPageStatus.Blocked"/>, and never after a networking
    /// interaction, so a technician who is typing keeps the focus.
    /// </summary>
    /// <param name="status">The new page status.</param>
    /// <param name="hasNetworkingInteraction">Whether the technician already interacted with networking.</param>
    /// <param name="canConnectProvisioned">Whether the provisioned Wi-Fi can be connected now.</param>
    /// <param name="hasVisibleNetworks">Whether the Wi-Fi list has networks.</param>
    /// <returns>The focus target.</returns>
    public static ConnectFocusTarget ResolveFocus(ConnectPageStatus status, bool hasNetworkingInteraction, bool canConnectProvisioned, bool hasVisibleNetworks) => status switch
    {
        ConnectPageStatus.Ready => ConnectFocusTarget.Continue,
        ConnectPageStatus.Blocked when hasNetworkingInteraction => ConnectFocusTarget.None,
        ConnectPageStatus.Blocked when canConnectProvisioned => ConnectFocusTarget.ProvisionedConnect,
        ConnectPageStatus.Blocked when hasVisibleNetworks => ConnectFocusTarget.WifiNetworks,
        ConnectPageStatus.Blocked => ConnectFocusTarget.Retry,
        _ => ConnectFocusTarget.None
    };
}
