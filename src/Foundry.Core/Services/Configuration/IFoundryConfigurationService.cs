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
}
