namespace Foundry.Models.Configuration.Deploy;

public sealed record DeployAutopilotSettings
{
    public bool IsEnabled { get; init; }
    public string? DefaultProfileFolderName { get; init; }
}
