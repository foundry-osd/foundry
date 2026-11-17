// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.WinPe;

namespace Foundry.Connect.Tests.Support;

/// <summary>
/// Writes Foundry boot media layouts for Connect loader tests with the Core generator.
/// </summary>
internal static class ConnectMediaFixtures
{
    /// <summary>The deployment password of the protected fixtures.</summary>
    public const string Password = "correct horse battery staple";

    /// <summary>
    /// Writes media protected by <see cref="Password"/>: the generated <c>Foundry\Config\foundry.winpe.config.json</c> and every
    /// staged asset at its media destination under <paramref name="directory"/>.
    /// </summary>
    /// <remarks>Authoring file references resolve to the file of the same name directly under <paramref name="directory"/>.</remarks>
    /// <returns>The configuration file path and a copy of the deployment key; the caller owns the copy.</returns>
    public static (string ConfigurationPath, byte[] DeploymentKey) WriteProtected(
        TemporaryDirectory directory,
        FoundryConfigurationDocument authoring,
        NetworkSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(authoring);
        ArgumentNullException.ThrowIfNull(secrets);
        var inputs = new MediaInputs
        {
            ResolveFile = reference => Path.Combine(directory.Path, reference.FileName) is var path && File.Exists(path) ? path : null,
            Network = secrets
        };

        using DeploymentMediaProtectionMaterial protection = DeploymentMediaProtectionService.CreateProtected(Password);
        WinPeConfigurationBundle bundle = new WinPeConfigurationGenerator().CreateBundle(
            authoring, inputs, Path.Combine(directory.Path, "Staging"), protection);
        foreach (FoundryConnectProvisionedAssetFile asset in bundle.ConnectAssetFiles)
        {
            directory.CreateFile(asset.RelativeDestinationPath, File.ReadAllBytes(asset.SourcePath));
        }

        string configurationPath = directory.CreateFile(Path.Combine("Foundry", "Config", "foundry.winpe.config.json"), bundle.ConfigurationJson);
        return (configurationPath, protection.DeploymentKey.ToArray());
    }

    /// <summary>Generates the unprotected WinPE runtime configuration of <paramref name="authoring"/> with the Core generator.</summary>
    /// <param name="authoring">The authoring configuration.</param>
    /// <param name="inputs">The referenced files and network passwords.</param>
    /// <param name="stagingDirectoryPath">The directory that receives the staged network assets.</param>
    /// <returns>The <c>foundry.winpe.config.json</c> content.</returns>
    public static string Generate(FoundryConfigurationDocument authoring, MediaInputs inputs, string stagingDirectoryPath)
    {
        using DeploymentMediaProtectionMaterial protection = DeploymentMediaProtectionService.CreateUnprotected();
        return new WinPeConfigurationGenerator().CreateBundle(authoring, inputs, stagingDirectoryPath, protection).ConfigurationJson;
    }
}
