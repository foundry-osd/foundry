namespace Foundry.Deploy.Models;

public sealed record AutopilotProfileCatalogItem
{
    public required string FolderName { get; init; }
    public required string DisplayName { get; init; }
    public required string ConfigurationFilePath { get; init; }
}
