namespace Foundry.Deploy.Models.Configuration;

public sealed record DeployCustomizationSettings
{
    public DeployMachineNamingSettings MachineNaming { get; init; } = new();
}
