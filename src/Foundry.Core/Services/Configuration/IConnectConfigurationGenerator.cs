// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Converts Foundry network settings into the runtime configuration and asset bundle consumed by Foundry.Connect.
/// </summary>
public interface IConnectConfigurationGenerator
{
    /// <summary>
    /// Generates the Foundry.Connect runtime configuration and restages any referenced runtime assets.
    /// </summary>
    /// <param name="document">The Foundry configuration source document.</param>
    /// <param name="inputs">Where referenced profiles and certificates can be read, and the network passwords.</param>
    /// <param name="stagingDirectoryPath">The directory whose FoundryConnectAssets child is deleted and recreated for copied assets.</param>
    /// <returns>The generated Foundry.Connect configuration document.</returns>
    FoundryConnectConfigurationDocument Generate(FoundryConfigurationDocument document, MediaInputs inputs, string stagingDirectoryPath);

    /// <summary>
    /// Creates a complete provisioning bundle for Foundry.Connect media and restages its copied assets.
    /// </summary>
    /// <param name="document">The Foundry configuration source document.</param>
    /// <param name="inputs">Where referenced profiles and certificates can be read, and the network passwords.</param>
    /// <param name="stagingDirectoryPath">The directory whose FoundryConnectAssets child is deleted and recreated for copied assets.</param>
    /// <param name="authoringVersion">Optional OSD release captured for this media build.</param>
    /// <param name="protection">
    /// The deployment protection material of the media build. Network secrets are encrypted with its deployment key only when
    /// Password protection is enabled; without it a configuration that provisions a network secret is refused.
    /// </param>
    /// <returns>The generated configuration, serialized JSON, and asset files.</returns>
    /// <exception cref="InvalidOperationException">The network configuration is invalid, including a network secret without protection.</exception>
    FoundryConnectProvisioningBundle CreateProvisioningBundle(
        FoundryConfigurationDocument document,
        MediaInputs inputs,
        string stagingDirectoryPath,
        string? authoringVersion = null,
        DeploymentMediaProtectionMaterial? protection = null);

    /// <summary>
    /// Serializes a Foundry.Connect configuration document to JSON.
    /// </summary>
    /// <param name="document">The Foundry.Connect configuration document.</param>
    /// <returns>The JSON representation.</returns>
    string Serialize(FoundryConnectConfigurationDocument document);
}
