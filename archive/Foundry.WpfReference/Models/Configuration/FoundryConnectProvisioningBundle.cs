namespace Foundry.Models.Configuration;

public sealed record FoundryConnectProvisioningBundle
{
    public string ConfigurationJson { get; init; } = string.Empty;

    public IReadOnlyList<FoundryConnectProvisionedAssetFile> AssetFiles { get; init; } = Array.Empty<FoundryConnectProvisionedAssetFile>();
}
