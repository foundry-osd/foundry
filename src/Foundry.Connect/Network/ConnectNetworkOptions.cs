// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Connect.Network;

/// <summary>
/// Runtime switches of the network layer, derived once from the media configuration.
/// </summary>
/// <param name="IsWifiFeatureEnabled">Whether Wi-Fi may be observed and used; otherwise Wi-Fi is <see cref="WifiAvailability.Disabled"/>.</param>
/// <param name="RefreshInterval">Interval between two network observations.</param>
public sealed record ConnectNetworkOptions(bool IsWifiFeatureEnabled, TimeSpan RefreshInterval)
{
    /// <summary>
    /// Gets the default interval between two network observations.
    /// </summary>
    public static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromSeconds(FoundryConnectApplicationInfo.DefaultRefreshIntervalSeconds);

    /// <summary>
    /// Creates the options for a media configuration.
    /// </summary>
    /// <remarks>
    /// Wi-Fi is enabled when the media provisions it or a debugger is attached, so the developer loop keeps
    /// Wi-Fi on unprovisioned media.
    /// </remarks>
    /// <param name="configuration">Media configuration.</param>
    /// <param name="isDebuggerAttached">Whether a debugger is attached to the process.</param>
    /// <returns>The options.</returns>
    public static ConnectNetworkOptions FromConfiguration(FoundryConnectConfigurationDocument configuration, bool isDebuggerAttached)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new(configuration.Capabilities.WifiProvisioned || isDebuggerAttached, DefaultRefreshInterval);
    }
}
