// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Connect.Network.Policy;
using Foundry.Core.Models.Configuration;

namespace Foundry.Connect.Telemetry;

/// <summary>
/// Builds the properties of the <c>connect:session_ready</c> event from the typed network state.
/// </summary>
/// <remarks>
/// Keys and value sets are the ones <c>TelemetryEventPropertyPolicy</c> allows (T17, L2-4), same keys as lot 2 except
/// <c>connect_network_layout_mode</c>, retired by the single page; the allowlist keeps it for older media. Only categories
/// and booleans are produced: never an SSID, a profile name or an address.
/// </remarks>
public static class ConnectSessionReadyTelemetry
{
    private const string Ethernet = "ethernet";
    private const string Wifi = "wifi";
    private const string Unknown = "unknown";
    private const string None = "none";

    /// <summary>
    /// Builds the event properties.
    /// </summary>
    /// <param name="state">Network state at exit; its internet status is expected to be <see cref="InternetStatus.Online"/>.</param>
    /// <param name="configuration">Runtime configuration.</param>
    /// <returns>The telemetry properties.</returns>
    public static IReadOnlyDictionary<string, object?> Build(NetworkState state, FoundryConnectConfigurationDocument configuration)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(configuration);

        WifiConnection? connection = state.ActiveInternetPath == InternetPath.Wifi ? state.Wifi.Connection : null;
        WifiAvailability availability = state.Wifi.Availability;

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["connect_network_connection_type"] = ConnectionType(state.ActiveInternetPath),
            ["connect_ethernet_available"] = state.Ethernet.HasAdapter,
            ["connect_wifi_available"] = availability == WifiAvailability.Available,
            ["connect_wifi_security_type"] = connection is null ? None : WifiSecurityClassifier.TelemetryCategory(connection.Security),
            ["connect_wifi_source"] = connection is null ? None : connection.Source == WifiConnectionSource.Provisioned ? "provisioned" : "manual",
            ["connect_wired_dot1x_enabled"] = configuration.Dot1x.IsEnabled,
            ["connect_wifi_provisioned"] = configuration.Capabilities.WifiProvisioned && configuration.Wifi.IsEnabled
        };
    }

    /// <summary>
    /// Gets the connection category of the active internet path, as used in the exit log entry.
    /// </summary>
    /// <param name="path">Active internet path.</param>
    /// <returns><c>ethernet</c>, <c>wifi</c> or <c>unknown</c>.</returns>
    public static string ConnectionType(InternetPath path) => path switch
    {
        InternetPath.Ethernet => Ethernet,
        InternetPath.Wifi => Wifi,
        _ => Unknown
    };
}
