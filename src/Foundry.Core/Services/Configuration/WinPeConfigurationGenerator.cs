// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Network;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Generates <c>foundry.winpe.config.json</c> (schema <see cref="ConfigurationSchemaVersions.WinPeCurrent"/>), the one runtime
/// configuration of a boot media, by composing the <see cref="ConnectConfigurationGenerator"/> and
/// <see cref="DeployConfigurationGenerator"/> sections under the shared telemetry, protection and network sections.
/// </summary>
/// <remarks>
/// <para>
/// The <c>protection</c> block of the media build is always written; an unprotected media writes its disabled block. Every
/// secret of the document (network passwords, client certificates, domain credentials, OOBE passwords) is an envelope keyed by
/// the deployment key that this block protects.
/// </para>
/// <para>
/// <see cref="Serialize"/> is the only JSON writer of the document: media preparation then binds and splits its <c>deploy</c>
/// node in place, and provisioning writes the result to <c>X:\Foundry\Config</c>.
/// </para>
/// </remarks>
public sealed class WinPeConfigurationGenerator
{
    private readonly ConnectConfigurationGenerator connectGenerator = new();
    private readonly DeployConfigurationGenerator deployGenerator = new();

    /// <summary>Generates the WinPE runtime configuration of one media build and stages its network assets.</summary>
    /// <param name="document">The Foundry configuration, with the telemetry the media reports.</param>
    /// <param name="inputs">The session inputs: referenced files, network, Autopilot and network cache secrets.</param>
    /// <param name="stagingDirectoryPath">The directory whose FoundryConnectAssets child is deleted and recreated for network assets.</param>
    /// <param name="protection">The deployment protection material of the media build.</param>
    /// <param name="oobeAccountSecretState">The transient OOBE account passwords, when any account is configured.</param>
    /// <param name="domainJoinSecretState">The transient domain join passwords, when automatic joining is configured.</param>
    /// <param name="authoringVersion">The OSD release that builds the media, when known.</param>
    /// <returns>The document, its serialized JSON and the staged network assets.</returns>
    /// <exception cref="InvalidOperationException">The configuration is not ready for deployment media.</exception>
    public WinPeConfigurationBundle CreateBundle(
        FoundryConfigurationDocument document,
        MediaInputs inputs,
        string stagingDirectoryPath,
        DeploymentMediaProtectionMaterial protection,
        OobeAccountSecretState? oobeAccountSecretState = null,
        DomainJoinSecretState? domainJoinSecretState = null,
        string? authoringVersion = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(protection);

        FoundryConnectProvisioningBundle connect = connectGenerator.CreateProvisioningBundle(document, inputs, stagingDirectoryPath, protection);
        var configuration = new FoundryWinPeConfigurationDocument
        {
            AuthoringVersion = authoringVersion,
            Telemetry = document.Telemetry,
            Protection = protection.Settings,
            Network = new WinPeNetworkSettings
            {
                ProfileRoaming = ConnectConfigurationGenerator.ToProfileRoaming(document.Network),
                ArtifactRootPath = NetworkProfileRoamingArtifacts.DefaultArtifactRootPath
            },
            Connect = connect.Configuration,
            Deploy = deployGenerator.Generate(
                document, protection.DeploymentKey, protection.Settings, oobeAccountSecretState, domainJoinSecretState, inputs)
        };

        return new WinPeConfigurationBundle
        {
            Configuration = configuration,
            ConfigurationJson = Serialize(configuration),
            ConnectAssetFiles = connect.AssetFiles
        };
    }

    /// <summary>Serializes a WinPE runtime configuration as it is written to boot media.</summary>
    /// <param name="document">The WinPE runtime configuration.</param>
    /// <returns>The indented camelCase JSON.</returns>
    public static string Serialize(FoundryWinPeConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, ConfigurationJsonDefaults.SerializerOptions);
    }
}
