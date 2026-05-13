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
    public FoundryConnectConfigurationDocument Generate(FoundryExpertConfigurationDocument document, string stagingDirectoryPath)
    {
        return CreateProvisioningBundle(document, stagingDirectoryPath).Configuration;
    }

    /// <inheritdoc />
    public FoundryConnectProvisioningBundle CreateProvisioningBundle(
        FoundryExpertConfigurationDocument document,
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
        byte[]? mediaSecretsKey = ResolveMediaSecretsKey(isWifiEnabled, wifi);
        SecretEnvelope? passphraseSecret = mediaSecretsKey is not null
            ? ConnectSecretEnvelopeProtector.Encrypt(wifi.Passphrase!, mediaSecretsKey)
            : null;

        // The generated document uses media-relative asset paths; source file paths stay outside the runtime JSON.
        FoundryConnectConfigurationDocument configuration = new()
        {
            Capabilities = new ConnectNetworkCapabilitiesSettings
            {
                WifiProvisioned = network.WifiProvisioned
            },
            Dot1x = dot1x with
            {
                ProfileTemplatePath = wiredProfileRelativePath,
                CertificatePath = wiredCertificateRelativePath,
                AuthenticationMode = NetworkAuthenticationMode.MachineOnly,
                AllowRuntimeCredentials = false
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
                AllowRuntimeCredentials = false
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

    private static byte[]? ResolveMediaSecretsKey(bool isWifiEnabled, WifiSettings wifi)
    {
        // Personal Wi-Fi is the only current flow that needs a media secret key.
        return isWifiEnabled &&
               !wifi.HasEnterpriseProfile &&
               !string.IsNullOrWhiteSpace(wifi.Passphrase)
            ? ConnectSecretEnvelopeProtector.GenerateMediaKey()
            : null;
    }

    private static string NormalizeEmbeddedRelativePath(string relativePath)
    {
        return relativePath.Replace('/', '\\').TrimStart('\\');
    }
}
