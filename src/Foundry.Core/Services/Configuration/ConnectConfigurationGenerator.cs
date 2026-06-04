using System.Text.Json;
using Foundry.Core.Models.Configuration;

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
    public FoundryConnectConfigurationDocument Generate(FoundryConfigurationDocument document, string stagingDirectoryPath)
    {
        return CreateProvisioningBundle(document, stagingDirectoryPath).Configuration;
    }

    /// <inheritdoc />
    public FoundryConnectProvisioningBundle CreateProvisioningBundle(
        FoundryConfigurationDocument document,
        string stagingDirectoryPath)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectoryPath);

        NetworkConfigurationValidationResult validationResult = NetworkConfigurationValidator.Validate(document.Network);
        if (!validationResult.IsValid)
        {
            throw new InvalidOperationException($"Network configuration is invalid: {validationResult.Code}.");
        }

        string assetRootPath = Path.Combine(stagingDirectoryPath, StagedAssetRootFolderName);
        EnsureDirectoryClean(assetRootPath);

        List<FoundryConnectProvisionedAssetFile> assetFiles = [];
        NetworkSettings network = document.Network;
        Dot1xSettings dot1x = network.Dot1x;
        WifiSettings wifi = network.Wifi;
        bool isWifiEnabled = network.WifiProvisioned && wifi.IsEnabled;

        string? wiredProfileRelativePath = dot1x.IsEnabled
            ? CopyAsset(dot1x.ProfileTemplatePath, assetRootPath, WiredProfileFolder, assetFiles)
            : null;
        string? wiredCertificateRelativePath = dot1x.IsEnabled
            ? CopyAsset(dot1x.CertificatePath, assetRootPath, WiredCertificateFolder, assetFiles)
            : null;
        string? wifiProfileRelativePath = isWifiEnabled && wifi.HasEnterpriseProfile
            ? CopyAsset(wifi.EnterpriseProfileTemplatePath, assetRootPath, WifiProfileFolder, assetFiles)
            : null;
        string? wifiCertificateRelativePath = isWifiEnabled
            ? CopyAsset(wifi.CertificatePath, assetRootPath, WifiCertificateFolder, assetFiles)
            : null;
        byte[]? mediaSecretsKey = ResolveMediaSecretsKey(isWifiEnabled, dot1x, wifi);
        SecretEnvelope? passphraseSecret = mediaSecretsKey is not null &&
            isWifiEnabled &&
            !wifi.HasEnterpriseProfile &&
            !string.IsNullOrWhiteSpace(wifi.Passphrase)
            ? ConnectSecretEnvelopeProtector.Encrypt(wifi.Passphrase!, mediaSecretsKey)
            : null;
        SecretEnvelope? wiredCertificatePfxPasswordSecret = CreateCertificatePfxPasswordSecret(
            dot1x.CertificatePath,
            dot1x.CertificatePfxPassword,
            mediaSecretsKey);
        SecretEnvelope? wifiCertificatePfxPasswordSecret = CreateCertificatePfxPasswordSecret(
            wifi.CertificatePath,
            wifi.CertificatePfxPassword,
            mediaSecretsKey);

        // The generated document uses media-relative asset paths; source file paths stay outside the runtime JSON.
        FoundryConnectConfigurationDocument configuration = new()
        {
            Capabilities = new ConnectNetworkCapabilitiesSettings
            {
                WifiProvisioned = network.WifiProvisioned
            },
            Network = new ConnectNetworkSettings
            {
                ProfileRoaming = new ConnectNetworkProfileRoamingSettings
                {
                    IsEnabled = network.RoamWifiProfilesToWindows,
                    IncludePrivateKeyMaterial = network.RoamPrivateKeyMaterialToWindows
                }
            },
            Dot1x = dot1x with
            {
                ProfileTemplatePath = wiredProfileRelativePath,
                CertificatePath = wiredCertificateRelativePath,
                AuthenticationMode = NetworkAuthenticationMode.MachineOnly,
                AllowRuntimeCredentials = false,
                CertificatePfxPassword = null,
                CertificatePfxPasswordSecret = wiredCertificatePfxPasswordSecret
            },
            Wifi = wifi with
            {
                IsEnabled = isWifiEnabled,
                SecurityType = isWifiEnabled
                    ? NetworkConfigurationValidator.NormalizeWifiSecurityType(wifi)
                    : null,
                Passphrase = null,
                PassphraseSecret = passphraseSecret,
                EnterpriseProfileTemplatePath = isWifiEnabled && wifi.HasEnterpriseProfile
                    ? wifiProfileRelativePath
                    : null,
                CertificatePath = wifiCertificateRelativePath,
                EnterpriseAuthenticationMode = NetworkAuthenticationMode.UserOnly,
                AllowRuntimeCredentials = false,
                CertificatePfxPassword = null,
                CertificatePfxPasswordSecret = wifiCertificatePfxPasswordSecret
            },
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

    private static void EnsureDirectoryClean(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
    }

    private static byte[]? ResolveMediaSecretsKey(bool isWifiEnabled, Dot1xSettings dot1x, WifiSettings wifi)
    {
        bool hasPersonalWifiPassphrase = isWifiEnabled &&
            !wifi.HasEnterpriseProfile &&
            !string.IsNullOrWhiteSpace(wifi.Passphrase);
        bool hasCertificatePfxPassword = HasCertificatePfxPassword(dot1x.CertificatePath, dot1x.CertificatePfxPassword) ||
            HasCertificatePfxPassword(wifi.CertificatePath, wifi.CertificatePfxPassword);

        return hasPersonalWifiPassphrase || hasCertificatePfxPassword
            ? ConnectSecretEnvelopeProtector.GenerateMediaKey()
            : null;
    }

    private static SecretEnvelope? CreateCertificatePfxPasswordSecret(
        string? certificatePath,
        string? certificatePfxPassword,
        byte[]? mediaSecretsKey)
    {
        return mediaSecretsKey is not null && HasCertificatePfxPassword(certificatePath, certificatePfxPassword)
            ? ConnectSecretEnvelopeProtector.Encrypt(certificatePfxPassword!, mediaSecretsKey)
            : null;
    }

    private static bool HasCertificatePfxPassword(string? certificatePath, string? certificatePfxPassword)
    {
        return IsPfxPath(certificatePath) && !string.IsNullOrWhiteSpace(certificatePfxPassword);
    }

    private static bool IsPfxPath(string? certificatePath)
    {
        if (string.IsNullOrWhiteSpace(certificatePath))
        {
            return false;
        }

        string extension = Path.GetExtension(certificatePath);
        return string.Equals(extension, ".pfx", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".p12", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeEmbeddedRelativePath(string relativePath)
    {
        return relativePath.Replace('/', '\\').TrimStart('\\');
    }
}
