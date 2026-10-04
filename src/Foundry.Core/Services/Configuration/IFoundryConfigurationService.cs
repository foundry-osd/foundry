// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Serializes and deserializes Foundry configuration documents.
/// </summary>
public interface IFoundryConfigurationService
{
    /// <summary>
    /// Serializes an Foundry configuration document to JSON.
    /// </summary>
    /// <param name="document">The document to serialize.</param>
    /// <returns>The JSON representation.</returns>
    string Serialize(FoundryConfigurationDocument document);

    /// <summary>
    /// Deserializes an Foundry configuration document from JSON.
    /// </summary>
    /// <param name="json">The JSON document content.</param>
    /// <returns>The deserialized document, or a default document when the JSON literal is <c>null</c>.</returns>
    FoundryConfigurationDocument Deserialize(string json);

    /// <summary>Loads a machine-local authoring draft, retaining a domain-changed catalog awaiting repair.</summary>
    /// <remarks>Only catalog/target domain mismatch is permitted; catalog integrity and exclusive provisioning still apply. External imports must use <see cref="Deserialize"/>.</remarks>
    FoundryConfigurationDocument DeserializeLocalAuthoringDraft(string json);
}
