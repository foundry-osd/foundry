namespace Foundry.Models.Configuration;

public sealed record MachineNamingSettings
{
    public bool IsEnabled { get; init; }
    public string? Prefix { get; init; }
    public bool AutoGenerateName { get; init; }
    public bool AllowManualSuffixEdit { get; init; } = true;
}
