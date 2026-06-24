// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Converts full Foundry configuration into the reduced document consumed by Foundry.Deploy.
/// </summary>
public interface IDeployConfigurationGenerator
{
    /// <summary>
    /// Generates the deployment runtime configuration from the user-authored Foundry configuration document.
    /// </summary>
    /// <param name="document">The Foundry configuration document.</param>
    /// <returns>The deployment runtime configuration.</returns>
    FoundryDeployConfigurationDocument Generate(FoundryConfigurationDocument document);

    /// <summary>
    /// Generates the deployment runtime configuration and encrypts media-only secrets when a media key is provided.
    /// </summary>
    /// <param name="document">The Foundry configuration document.</param>
    /// <param name="mediaSecretsKey">Optional media secret key used for generated boot media secrets.</param>
    /// <returns>The deployment runtime configuration.</returns>
    FoundryDeployConfigurationDocument Generate(FoundryConfigurationDocument document, byte[]? mediaSecretsKey);

    /// <summary>
    /// Serializes a deployment runtime configuration document to JSON.
    /// </summary>
    /// <param name="document">The deployment runtime document.</param>
    /// <returns>The JSON representation.</returns>
    string Serialize(FoundryDeployConfigurationDocument document);
}
