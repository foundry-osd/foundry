namespace Foundry.Core.Models.Configuration;

public sealed record FoundryConnectProvisionedAssetFile
{
    public string SourcePath { get; init; } = string.Empty;

    public string RelativeDestinationPath { get; init; } = string.Empty;
}
