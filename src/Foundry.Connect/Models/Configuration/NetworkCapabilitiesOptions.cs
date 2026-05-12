namespace Foundry.Connect.Models.Configuration;

/// <summary>
/// Captures provisioned network capabilities that control runtime UI features.
/// </summary>
public sealed class NetworkCapabilitiesOptions
{
    /// <summary>
    /// Gets a value indicating whether runtime Wi-Fi features are enabled for the generated media.
    /// This gates both provisioned Wi-Fi bootstrap and discovered-network connection actions.
    /// </summary>
    public bool WifiProvisioned { get; init; }
}
