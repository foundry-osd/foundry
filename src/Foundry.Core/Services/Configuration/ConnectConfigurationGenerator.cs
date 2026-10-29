// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Network;
using Foundry.Core.Services.Autopilot;
using Foundry.Utilities.IO;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Generates Foundry.Connect runtime configuration and stages network assets for boot media.
/// </summary>
public sealed class ConnectConfigurationGenerator : IConnectConfigurationGenerator
{
    private const string StagedAssetRootFolderName = "FoundryConnectAssets";
    private const string MediaConfigRoot = @"Foundry\Config";
    private const string WiredProfileFolder = @"Network\Wired\Profiles";
    private const string WifiProfileFolder = @"Network\Wifi\Profiles";
    private const string WiredCertificateFolder = @"Network\Certificates\Wired";
    private const string WifiCertificateFolder = @"Network\Certificates\Wifi";

    /// <inheritdoc />
    public FoundryConnectConfigurationDocument Generate(FoundryConfigurationDocument document, MediaInputs inputs, string stagingDirectoryPath)
    {
        return CreateProvisioningBundle(document, inputs, stagingDirectoryPath).Configuration;
    }

    /// <inheritdoc />
    public FoundryConnectProvisioningBundle CreateProvisioningBundle(
        FoundryConfigurationDocument document,
        MediaInputs inputs,
        string stagingDirectoryPath,
        string? authoringVersion = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectoryPath);

        NetworkConfigurationValidationResult validationResult = NetworkConfigurationValidator.Validate(document.Network, inputs);
        if (!validationResult.IsValid)
        {
            throw new InvalidOperationException($"Network configuration is invalid: {validationResult.Code}.");
        }

        string assetRootPath = Path.Combine(stagingDirectoryPath, StagedAssetRootFolderName);
        DirectoryOperations.Recreate(assetRootPath);

        List<FoundryConnectProvisionedAssetFile> assetFiles = [];
        NetworkSettings network = document.Network;
        Dot1xSettings dot1x = network.Dot1x;
        WifiSettings wifi = network.Wifi;
        NetworkSecrets secrets = inputs.Network;
        bool isWifiEnabled = network.WifiProvisioned && wifi.IsEnabled;
        string? wiredCertificatePath = dot1x.IsEnabled ? inputs.ResolveRequired(dot1x.ClientCertificate ?? dot1x.TrustedRootCertificates.FirstOrDefault()) : null;
        string? wifiCertificatePath = isWifiEnabled ? inputs.ResolveRequired(wifi.ClientCertificate ?? wifi.TrustedRootCertificates.FirstOrDefault()) : null;

        string? wiredProfileRelativePath = dot1x.IsEnabled
            ? CopyAsset(inputs.ResolveRequired(dot1x.ProfileTemplate), assetRootPath, WiredProfileFolder, assetFiles)
            : null;
        string? wiredCertificateRelativePath = CopyAsset(wiredCertificatePath, assetRootPath, WiredCertificateFolder, assetFiles);
        string? wifiProfileRelativePath = isWifiEnabled && wifi.HasEnterpriseProfile
            ? CopyAsset(inputs.ResolveRequired(wifi.EnterpriseProfileTemplate), assetRootPath, WifiProfileFolder, assetFiles)
            : null;
        string? wifiCertificateRelativePath = CopyAsset(wifiCertificatePath, assetRootPath, WifiCertificateFolder, assetFiles);
        byte[]? mediaSecretsKey = ResolveMediaSecretsKey(isWifiEnabled, wifi, secrets, wiredCertificatePath, wifiCertificatePath);
        SecretEnvelope? passphraseSecret = mediaSecretsKey is not null &&
            isWifiEnabled &&
            !wifi.HasEnterpriseProfile &&
            !string.IsNullOrWhiteSpace(secrets.WifiPassphrase)
            ? MediaSecretEnvelopeProtector.EncryptString(secrets.WifiPassphrase!, mediaSecretsKey)
            : null;
        SecretEnvelope? wiredCertificatePfxPasswordSecret = CreateCertificatePfxPasswordSecret(
            wiredCertificatePath,
            secrets.WiredCertificatePfxPassword,
            mediaSecretsKey);
        SecretEnvelope? wifiCertificatePfxPasswordSecret = CreateCertificatePfxPasswordSecret(
            wifiCertificatePath,
            secrets.WifiCertificatePfxPassword,
            mediaSecretsKey);

        // The generated document uses media-relative asset paths; source file paths stay outside the runtime JSON.
        FoundryConnectConfigurationDocument configuration = new()
        {
            AuthoringVersion = authoringVersion,
            Capabilities = new ConnectNetworkCapabilitiesSettings
            {
                WifiProvisioned = network.WifiProvisioned
            },
            Network = new ConnectNetworkSettings
            {
                ProfileRoaming = new ConnectNetworkProfileRoamingSettings
                {
                    WiredDot1x = new NetworkProfileRoamingTransportSettings
                    {
                        IsEnabled = network.RoamWiredDot1xProfileToWindows,
                        IncludePrivateKeyMaterial = network.RoamWiredDot1xPrivateKeyMaterialToWindows
                    },
                    Wifi = new NetworkProfileRoamingTransportSettings
                    {
                        IsEnabled = network.RoamWifiProfileToWindows,
                        IncludePrivateKeyMaterial = network.RoamWifiPrivateKeyMaterialToWindows
                    }
                }
            },
            Dot1x = ToRuntime(dot1x, wiredProfileRelativePath, wiredCertificateRelativePath, wiredCertificatePfxPasswordSecret),
            Wifi = ToRuntime(wifi, isWifiEnabled, wifiProfileRelativePath, wifiCertificateRelativePath, passphraseSecret, wifiCertificatePfxPasswordSecret),
            Telemetry = document.Telemetry
        };

        return new FoundryConnectProvisioningBundle
        {
            Configuration = configuration,
            ConfigurationJson = Serialize(configuration),
            MediaSecretsKey = mediaSecretsKey,
            AssetFiles = assetFiles
        };
    }

    /// <inheritdoc />
    public string Serialize(FoundryConnectConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, ConfigurationJsonDefaults.SerializerOptions);
    }

    /// <summary>
    /// Maps authoring wired 802.1X settings to the Connect runtime record with staged media-relative paths.
    /// </summary>
    /// <remarks>The runtime always uses machine-only authentication without runtime credentials; plaintext passwords are never copied.</remarks>
    /// <param name="authoring">The authoring settings.</param>
    /// <param name="profileTemplatePath">The media-relative staged profile path, or <see langword="null"/> when none is staged.</param>
    /// <param name="certificatePath">The media-relative staged certificate path, or <see langword="null"/> when none is staged.</param>
    /// <param name="certificatePfxPasswordSecret">The encrypted PFX password envelope, when one is required.</param>
    internal static ConnectDot1xSettings ToRuntime(
        Dot1xSettings authoring,
        string? profileTemplatePath,
        string? certificatePath,
        SecretEnvelope? certificatePfxPasswordSecret)
    {
        ArgumentNullException.ThrowIfNull(authoring);
        return new ConnectDot1xSettings
        {
            IsEnabled = authoring.IsEnabled,
            ProfileTemplatePath = profileTemplatePath,
            AuthenticationMode = NetworkAuthenticationMode.MachineOnly,
            AllowRuntimeCredentials = false,
            RequiresCertificate = authoring.RequiresCertificate,
            CertificatePath = certificatePath,
            CertificatePfxPasswordSecret = certificatePfxPasswordSecret
        };
    }

    /// <summary>
    /// Maps authoring Wi-Fi settings to the Connect runtime record with staged media-relative paths.
    /// </summary>
    /// <remarks>
    /// The security type is normalized only when Wi-Fi is provisioned, the enterprise profile path is kept only for an enterprise profile,
    /// and the runtime always uses user-only enterprise authentication without runtime credentials. Plaintext secrets are never copied.
    /// </remarks>
    /// <param name="authoring">The authoring settings.</param>
    /// <param name="isEnabled">Whether Wi-Fi is both provisioned on the media and enabled.</param>
    /// <param name="enterpriseProfileTemplatePath">The media-relative staged enterprise profile path, when one is staged.</param>
    /// <param name="certificatePath">The media-relative staged certificate path, when one is staged.</param>
    /// <param name="passphraseSecret">The encrypted personal passphrase envelope, when one is required.</param>
    /// <param name="certificatePfxPasswordSecret">The encrypted PFX password envelope, when one is required.</param>
    internal static ConnectWifiSettings ToRuntime(
        WifiSettings authoring,
        bool isEnabled,
        string? enterpriseProfileTemplatePath,
        string? certificatePath,
        SecretEnvelope? passphraseSecret,
        SecretEnvelope? certificatePfxPasswordSecret)
    {
        ArgumentNullException.ThrowIfNull(authoring);
        return new ConnectWifiSettings
        {
            IsEnabled = isEnabled,
            Ssid = authoring.Ssid,
            SecurityType = isEnabled ? NetworkConfigurationValidator.NormalizeWifiSecurityType(authoring, hasPassphrase: passphraseSecret is not null) : null,
            PassphraseSecret = passphraseSecret,
            HasEnterpriseProfile = authoring.HasEnterpriseProfile,
            EnterpriseProfileTemplatePath = isEnabled && authoring.HasEnterpriseProfile ? enterpriseProfileTemplatePath : null,
            EnterpriseAuthenticationMode = NetworkAuthenticationMode.UserOnly,
            AllowRuntimeCredentials = false,
            RequiresCertificate = authoring.RequiresCertificate,
            CertificatePath = certificatePath,
            CertificatePfxPasswordSecret = certificatePfxPasswordSecret
        };
    }

    private static string? CopyAsset(
        string? sourcePath,
        string assetRootPath,
        string relativeConfigFolder,
        ICollection<FoundryConnectProvisionedAssetFile> assetFiles)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return null;
        }

        string fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException($"Foundry Connect asset source file was not found: '{fullSourcePath}'.", fullSourcePath);
        }

        string fileName = Path.GetFileName(fullSourcePath);
        string stagedRelativePath = Path.Combine(relativeConfigFolder, fileName);
        string stagedFilePath = Path.Combine(assetRootPath, stagedRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(stagedFilePath)!);
        File.Copy(fullSourcePath, stagedFilePath, overwrite: true);

        assetFiles.Add(new FoundryConnectProvisionedAssetFile
        {
            SourcePath = stagedFilePath,
            RelativeDestinationPath = NormalizeEmbeddedRelativePath(Path.Combine(MediaConfigRoot, stagedRelativePath))
        });

        return NormalizeEmbeddedRelativePath(stagedRelativePath);
    }

    private static byte[]? ResolveMediaSecretsKey(
        bool isWifiEnabled,
        WifiSettings wifi,
        NetworkSecrets secrets,
        string? wiredCertificatePath,
        string? wifiCertificatePath)
    {
        bool hasPersonalWifiPassphrase = isWifiEnabled &&
            !wifi.HasEnterpriseProfile &&
            !string.IsNullOrWhiteSpace(secrets.WifiPassphrase);
        bool hasCertificatePfxPassword = HasCertificatePfxPassword(wiredCertificatePath, secrets.WiredCertificatePfxPassword) ||
            HasCertificatePfxPassword(wifiCertificatePath, secrets.WifiCertificatePfxPassword);

        return hasPersonalWifiPassphrase || hasCertificatePfxPassword
            ? MediaSecretEnvelopeProtector.GenerateMediaKey()
            : null;
    }

    private static SecretEnvelope? CreateCertificatePfxPasswordSecret(
        string? certificatePath,
        string? certificatePfxPassword,
        byte[]? mediaSecretsKey)
    {
        return mediaSecretsKey is not null && HasCertificatePfxPassword(certificatePath, certificatePfxPassword)
            ? MediaSecretEnvelopeProtector.EncryptString(certificatePfxPassword!, mediaSecretsKey)
            : null;
    }

    private static bool HasCertificatePfxPassword(string? certificatePath, string? certificatePfxPassword)
    {
        return !string.IsNullOrWhiteSpace(certificatePath)
            && NetworkProfileRoamingArtifacts.IsPfxPath(certificatePath)
            && !string.IsNullOrWhiteSpace(certificatePfxPassword);
    }

    private static string NormalizeEmbeddedRelativePath(string relativePath)
    {
        return relativePath.Replace('/', '\\').TrimStart('\\');
    }
}
