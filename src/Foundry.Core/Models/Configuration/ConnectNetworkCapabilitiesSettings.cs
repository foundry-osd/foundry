namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Captures capability flags emitted into the Foundry.Connect runtime configuration.
/// </summary>
public sealed record ConnectNetworkCapabilitiesSettings
{
    /// <summary>
    /// Gets a value indicating whether generated media should expose runtime Wi-Fi features.
    /// Active profile availability still depends on the generated Wi-Fi settings.
    /// </summary>
    public bool WifiProvisioned { get; init; }
}
