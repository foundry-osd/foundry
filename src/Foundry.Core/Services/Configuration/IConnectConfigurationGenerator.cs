using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Converts Expert Deploy network settings into the runtime configuration and asset bundle consumed by Foundry.Connect.
/// </summary>
public interface IConnectConfigurationGenerator
{
    /// <summary>
    /// Generates the Foundry.Connect runtime configuration.
    /// </summary>
    /// <param name="document">The Expert Deploy source document.</param>
    /// <param name="stagingDirectoryPath">The directory used to stage copied profile and certificate assets.</param>
    /// <returns>The generated Foundry.Connect configuration document.</returns>
    FoundryConnectConfigurationDocument Generate(FoundryExpertConfigurationDocument document, string stagingDirectoryPath);

    /// <summary>
    /// Creates a complete provisioning bundle for Foundry.Connect media.
    /// </summary>
    /// <param name="document">The Expert Deploy source document.</param>
    /// <param name="stagingDirectoryPath">The directory used to stage copied profile and certificate assets.</param>
    /// <returns>The generated configuration, serialized JSON, media secret key, and asset files.</returns>
    FoundryConnectProvisioningBundle CreateProvisioningBundle(FoundryExpertConfigurationDocument document, string stagingDirectoryPath);

    /// <summary>
    /// Serializes a Foundry.Connect configuration document to JSON.
    /// </summary>
    /// <param name="document">The Foundry.Connect configuration document.</param>
    /// <returns>The JSON representation.</returns>
    string Serialize(FoundryConnectConfigurationDocument document);
}
