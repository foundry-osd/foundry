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
    /// Gets AppX package display names passed to provisioned package removal.
    /// </summary>
    public IReadOnlyList<string> PackageNames { get; init; } = [];
}
