namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>
/// Describes provisioned AppX removal settings consumed by Foundry.Deploy.
/// </summary>
public sealed record DeployAppxRemovalSettings
{
    /// <summary>
    /// Gets whether provisioned AppX removal should run before OOBE.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets provisioned AppX package identifiers passed to package removal.
    /// </summary>
    public IReadOnlyList<string> PackageNames { get; init; } = [];
}
