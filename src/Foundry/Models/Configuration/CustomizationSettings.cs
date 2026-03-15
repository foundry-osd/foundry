namespace Foundry.Models.Configuration;

public sealed record CustomizationSettings
{
    public MachineNamingSettings MachineNaming { get; init; } = new();
}
