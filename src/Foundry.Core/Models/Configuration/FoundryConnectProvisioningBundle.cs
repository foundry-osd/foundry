namespace Foundry.Core.Models.Configuration;

public sealed record FoundryConnectProvisioningBundle
{
    public FoundryConnectConfigurationDocument Configuration { get; init; } = new();

    public string ConfigurationJson { get; init; } = string.Empty;

    public IReadOnlyList<FoundryConnectProvisionedAssetFile> AssetFiles { get; init; } = Array.Empty<FoundryConnectProvisionedAssetFile>();
}
