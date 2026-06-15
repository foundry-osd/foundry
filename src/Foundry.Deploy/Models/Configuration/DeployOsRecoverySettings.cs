namespace Foundry.Deploy.Models.Configuration;

/// <summary>
/// Describes whether Foundry.Deploy should provision the Foundry OS Recovery WinRE entry.
/// </summary>
public sealed record DeployOsRecoverySettings
{
    /// <summary>
    /// Gets a value indicating whether OS Recovery provisioning is enabled.
    /// </summary>
    public bool IsEnabled { get; init; }
}
