namespace Foundry.Core.Models.Configuration;

public sealed record CustomizationSettings
{
    public MachineNamingSettings MachineNaming { get; init; } = new();
}
