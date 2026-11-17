// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Converts Foundry network settings into the <c>connect</c> section of the WinPE runtime configuration and its staged assets.
/// </summary>
public interface IConnectConfigurationGenerator
{
    /// <summary>
    /// Creates the Connect section of a media build and restages its copied assets.
    /// </summary>
    /// <param name="document">The Foundry configuration source document.</param>
    /// <param name="inputs">Where referenced profiles and certificates can be read, and the network passwords.</param>
    /// <param name="stagingDirectoryPath">The directory whose FoundryConnectAssets child is deleted and recreated for copied assets.</param>
    /// <param name="protection">
    /// The deployment protection material of the media build. Network secrets are encrypted with its deployment key only when
    /// Password protection is enabled; without it a configuration that provisions a network secret is refused.
    /// </param>
    /// <returns>The generated <c>connect</c> section and its asset files.</returns>
    /// <exception cref="InvalidOperationException">The network configuration is invalid, including a network secret without protection.</exception>
    FoundryConnectProvisioningBundle CreateProvisioningBundle(
        FoundryConfigurationDocument document,
        MediaInputs inputs,
        string stagingDirectoryPath,
        DeploymentMediaProtectionMaterial? protection = null);
}
