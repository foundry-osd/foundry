// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Converts full Foundry configuration into the <c>deploy</c> section of the WinPE runtime configuration.
/// </summary>
public interface IDeployConfigurationGenerator
{
    /// <summary>
    /// Generates the deployment runtime configuration from the user-authored Foundry configuration document.
    /// </summary>
    /// <param name="document">The Foundry configuration document.</param>
    /// <returns>The deployment runtime configuration.</returns>
    DeployRuntimeSettings Generate(FoundryConfigurationDocument document);

    /// <summary>
    /// Generates the deployment runtime configuration and encrypts media-only secrets when a media key is provided.
    /// </summary>
    /// <param name="document">The Foundry configuration document.</param>
    /// <param name="deploymentSecretsKey">Optional Deploy secret key used for generated boot media secrets.</param>
    /// <returns>The deployment runtime configuration.</returns>
    DeployRuntimeSettings Generate(FoundryConfigurationDocument document, byte[]? deploymentSecretsKey);

    /// <summary>
    /// Generates the deployment runtime configuration with Deploy secret key and protection metadata.
    /// </summary>
    /// <param name="document">The Foundry configuration document.</param>
    /// <param name="deploymentSecretsKey">Optional Deploy secret key used for generated boot media secrets.</param>
    /// <param name="protectionSettings">Deployment media protection metadata.</param>
    /// <returns>The deployment runtime configuration.</returns>
    DeployRuntimeSettings Generate(
        FoundryConfigurationDocument document,
        byte[]? deploymentSecretsKey,
        DeployProtectionSettings? protectionSettings);

    /// <summary>
    /// Generates the deployment runtime configuration with Deploy secret key, protection metadata, and transient OOBE account secrets.
    /// </summary>
    /// <param name="document">The Foundry configuration document.</param>
    /// <param name="deploymentSecretsKey">Optional Deploy secret key used for generated boot media secrets.</param>
    /// <param name="protectionSettings">Deployment media protection metadata.</param>
    /// <param name="oobeAccountSecretState">Transient OOBE account password state merged only for generation.</param>
    /// <returns>The deployment runtime configuration.</returns>
    DeployRuntimeSettings Generate(
        FoundryConfigurationDocument document,
        byte[]? deploymentSecretsKey,
        DeployProtectionSettings? protectionSettings,
        OobeAccountSecretState? oobeAccountSecretState);

    /// <summary>Generates active domain metadata and context-bound credentials using the existing media protection.</summary>
    DeployRuntimeSettings Generate(
        FoundryConfigurationDocument document,
        byte[]? deploymentSecretsKey,
        DeployProtectionSettings? protectionSettings,
        OobeAccountSecretState? oobeAccountSecretState,
        DomainJoinSecretState? domainJoinSecretState);

    /// <summary>
    /// Generates the deployment runtime configuration with session inputs kept outside the document: the readable copy of the
    /// Autopilot boot media PFX and its password.
    /// </summary>
    DeployRuntimeSettings Generate(
        FoundryConfigurationDocument document,
        byte[]? deploymentSecretsKey,
        DeployProtectionSettings? protectionSettings,
        OobeAccountSecretState? oobeAccountSecretState,
        DomainJoinSecretState? domainJoinSecretState,
        MediaInputs inputs);

    /// <summary>
    /// Serializes a <c>deploy</c> section to JSON.
    /// </summary>
    /// <param name="document">The <c>deploy</c> section.</param>
    /// <returns>The JSON representation.</returns>
    string Serialize(DeployRuntimeSettings document);
}
