namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>
/// Describes customization settings consumed by Foundry.Deploy.
/// </summary>
public sealed record DeployCustomizationSettings
{
    /// <summary>
    /// Gets computer-name customization rules.
    /// </summary>
    public DeployMachineNamingSettings MachineNaming { get; init; } = new();
}
