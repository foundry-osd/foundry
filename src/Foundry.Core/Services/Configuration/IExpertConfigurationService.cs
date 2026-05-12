using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Serializes and deserializes Expert Deploy configuration documents.
/// </summary>
public interface IExpertConfigurationService
{
    /// <summary>
    /// Serializes an Expert Deploy configuration document to JSON.
    /// </summary>
    /// <param name="document">The document to serialize.</param>
    /// <returns>The JSON representation.</returns>
    string Serialize(FoundryExpertConfigurationDocument document);

    /// <summary>
    /// Deserializes an Expert Deploy configuration document from JSON.
    /// </summary>
    /// <param name="json">The JSON document content.</param>
    /// <returns>The deserialized Expert Deploy configuration document.</returns>
    FoundryExpertConfigurationDocument Deserialize(string json);
}
