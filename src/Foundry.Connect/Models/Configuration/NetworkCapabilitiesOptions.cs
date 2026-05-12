namespace Foundry.Connect.Models.Configuration;

/// <summary>
/// Captures provisioned network capabilities that control runtime UI features.
/// </summary>
public sealed class NetworkCapabilitiesOptions
{
    /// <summary>
    /// Gets a value indicating whether generated media contains a provisioned Wi-Fi configuration.
    /// </summary>
    public bool WifiProvisioned { get; init; }
}
