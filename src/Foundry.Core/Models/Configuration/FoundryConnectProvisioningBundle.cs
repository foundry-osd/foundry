namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Represents all files and secrets required to provision Foundry.Connect on boot media.
/// </summary>
public sealed record FoundryConnectProvisioningBundle
{
    /// <summary>
    /// Gets the generated Foundry.Connect configuration model.
    /// </summary>
    public FoundryConnectConfigurationDocument Configuration { get; init; } = new();

    /// <summary>
    /// Gets the serialized configuration JSON written to media.
    /// </summary>
    public string ConfigurationJson { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional secret key written beside encrypted media secrets.
    /// </summary>
    public byte[]? MediaSecretsKey { get; init; }

    /// <summary>
    /// Gets additional asset files staged beside the configuration.
    /// </summary>
    public IReadOnlyList<FoundryConnectProvisionedAssetFile> AssetFiles { get; init; } = Array.Empty<FoundryConnectProvisionedAssetFile>();
}
