namespace Foundry.Deploy.Models.Configuration;

public sealed record DeployAutopilotSettings
{
    public bool IsEnabled { get; init; }
    public string? DefaultProfileFolderName { get; init; }
}
