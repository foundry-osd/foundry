namespace Foundry.Deploy.Models.Configuration;

public sealed record DeployCustomizationSettings
{
    /// <summary>
    /// Gets computer-name customization rules.
    /// </summary>
    public DeployMachineNamingSettings MachineNaming { get; init; } = new();

    /// <summary>
    /// Gets Windows OOBE customization settings applied during deployment.
    /// </summary>
    public DeployOobeSettings Oobe { get; init; } = new();
}
