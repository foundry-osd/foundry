// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Wpf.Network;

/// <summary>
/// Connection state shown by the icon next to the Network menu item of the Windows PE menu bars.
/// </summary>
public enum NetworkIndicatorState
{
    /// <summary>No usable network connection.</summary>
    Disconnected,

    /// <summary>Connected through a wired adapter.</summary>
    Ethernet,

    /// <summary>Connected through a Wi-Fi adapter.</summary>
    Wifi,

    /// <summary>Connected to a network without confirmed internet access (for example behind a captive portal).</summary>
    Limited
}
