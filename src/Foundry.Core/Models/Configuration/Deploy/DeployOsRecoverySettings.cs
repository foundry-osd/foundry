namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>
/// Describes whether Foundry.Deploy should expect Windows RE OS recovery integration.
/// </summary>
public sealed record DeployOsRecoverySettings
{
    /// <summary>
    /// Gets a value indicating whether the OS recovery integration is enabled.
    /// </summary>
    public bool IsEnabled { get; init; }
}
