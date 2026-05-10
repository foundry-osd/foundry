namespace Foundry.Core.Models.Configuration;

public sealed record AutopilotSettings
{
    public bool IsEnabled { get; init; }
    public string? DefaultProfileId { get; init; }
    public IReadOnlyList<AutopilotProfileSettings> Profiles { get; init; } = [];
}
