namespace Foundry.Models.Configuration.Deploy;

public sealed record DeployMachineNamingSettings
{
    public bool IsEnabled { get; init; }
    public string? Prefix { get; init; }
    public bool AutoGenerateName { get; init; }
    public bool AllowManualSuffixEdit { get; init; } = true;
}
