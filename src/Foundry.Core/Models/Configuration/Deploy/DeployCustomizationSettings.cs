namespace Foundry.Core.Models.Configuration.Deploy;

public sealed record DeployCustomizationSettings
{
    public DeployMachineNamingSettings MachineNaming { get; init; } = new();
}
