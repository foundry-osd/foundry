namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Captures capability flags emitted into the Foundry.Connect runtime configuration.
/// </summary>
public sealed record ConnectNetworkCapabilitiesSettings
{
    /// <summary>
    /// Gets a value indicating whether the media contains a provisioned Wi-Fi profile.
    /// </summary>
    public bool WifiProvisioned { get; init; }
}
