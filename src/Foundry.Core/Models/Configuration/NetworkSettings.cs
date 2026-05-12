namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes the network capabilities and profiles to stage on Foundry.Connect media.
/// </summary>
public sealed record NetworkSettings
{
    /// <summary>
    /// Gets whether Wi-Fi provisioning should be considered available on the target media.
    /// </summary>
    public bool WifiProvisioned { get; init; }

    /// <summary>
    /// Gets wired 802.1X provisioning settings.
    /// </summary>
    public Dot1xSettings Dot1x { get; init; } = new();

    /// <summary>
    /// Gets Wi-Fi provisioning settings.
    /// </summary>
    public WifiSettings Wifi { get; init; } = new();
}
