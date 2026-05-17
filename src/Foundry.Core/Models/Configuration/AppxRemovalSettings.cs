namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Stores user-authored provisioned AppX removal settings.
/// </summary>
public sealed record AppxRemovalSettings
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
