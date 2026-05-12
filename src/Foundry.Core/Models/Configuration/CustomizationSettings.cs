namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Stores user-authored deployment customization settings.
/// </summary>
public sealed record CustomizationSettings
{
    /// <summary>
    /// Gets hostname generation settings for deployed machines.
    /// </summary>
    public MachineNamingSettings MachineNaming { get; init; } = new();
}
