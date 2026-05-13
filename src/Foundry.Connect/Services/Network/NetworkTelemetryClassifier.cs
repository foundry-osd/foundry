using Foundry.Connect.Models.Network;

namespace Foundry.Connect.Services.Network;

/// <summary>
/// Converts runtime network details into stable telemetry categories.
/// </summary>
internal static class NetworkTelemetryClassifier
{
    /// <summary>
    /// Classifies the active connection type without exposing adapter or network identifiers.
    /// </summary>
    /// <param name="snapshot">Current network status snapshot.</param>
    /// <returns>A stable connection category.</returns>
    public static string ClassifyConnection(NetworkStatusSnapshot snapshot)
    {
        if (snapshot.IsEthernetConnected)
        {
            return "ethernet";
        }

        return string.IsNullOrWhiteSpace(snapshot.ConnectedWifiSsid) ? "unknown" : "wifi";
    }

    /// <summary>
    /// Classifies a native WLAN authentication label into a low-cardinality security category.
    /// </summary>
    /// <param name="authentication">Native authentication label.</param>
    /// <returns>A stable Wi-Fi security category.</returns>
    public static string ClassifyWifiSecurity(string? authentication)
    {
        if (string.IsNullOrWhiteSpace(authentication))
        {
            return "none";
        }

        string normalized = authentication.Trim();
        if (normalized.Contains("OWE", StringComparison.OrdinalIgnoreCase))
        {
            return "owe";
        }

        if (normalized.Contains("Open", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return "open";
        }

        if (normalized.Contains("Enterprise", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("802.1X", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("EAP", StringComparison.OrdinalIgnoreCase))
        {
            return "enterprise";
        }

        if (normalized.Contains("Personal", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("PSK", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("SAE", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("WEP", StringComparison.OrdinalIgnoreCase))
        {
            return "personal";
        }

        return "unknown";
    }
}
