namespace Foundry.Deploy.Models.Configuration;

/// <summary>
/// Describes network settings consumed by Foundry.Deploy.
/// </summary>
public sealed record DeployNetworkSettings
{
    /// <summary>
    /// Gets network profile roaming settings.
    /// </summary>
    public DeployNetworkProfileRoamingSettings ProfileRoaming { get; init; } = new();
}
