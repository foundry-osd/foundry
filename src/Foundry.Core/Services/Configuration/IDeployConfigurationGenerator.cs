using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Converts full Expert Deploy configuration into the reduced document consumed by Foundry.Deploy.
/// </summary>
public interface IDeployConfigurationGenerator
{
    /// <summary>
    /// Generates the deployment runtime configuration from the user-authored Expert Deploy document.
    /// </summary>
    /// <param name="document">The Expert Deploy document.</param>
    /// <returns>The deployment runtime configuration.</returns>
    FoundryDeployConfigurationDocument Generate(FoundryExpertConfigurationDocument document);

    /// <summary>
    /// Serializes a deployment runtime configuration document to JSON.
    /// </summary>
    /// <param name="document">The deployment runtime document.</param>
    /// <returns>The JSON representation.</returns>
    string Serialize(FoundryDeployConfigurationDocument document);
}
