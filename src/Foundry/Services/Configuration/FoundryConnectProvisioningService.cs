using System.Text.Json;
using Foundry.Models.Configuration;

namespace Foundry.Services.Configuration;

public sealed class FoundryConnectProvisioningService : IFoundryConnectProvisioningService
{
    public FoundryConnectProvisioningBundle Prepare(FoundryExpertConfigurationDocument document, string stagingDirectoryPath)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectoryPath);

        string assetRootPath = Path.Combine(stagingDirectoryPath, "FoundryConnectAssets");
        EnsureDirectoryClean(assetRootPath);

        List<FoundryConnectProvisionedAssetFile> assetFiles = [];
        Dot1xSettings dot1x = document.Network.Dot1x;
        WifiSettings wifi = document.Network.Wifi;

        ValidateNetworkSettings(document.Network);

        string? wiredProfileRelativePath = CopyOptionalAsset(dot1x.ProfileTemplatePath, assetRootPath, Path.Combine("Network", "Wired", "Profiles"), assetFiles);
        string? wiredCertificateRelativePath = CopyOptionalAsset(dot1x.CertificatePath, assetRootPath, Path.Combine("Network", "Certificates", "Wired"), assetFiles);
        string? wifiCertificateRelativePath = wifi.IsEnabled
            ? CopyOptionalAsset(wifi.CertificatePath, assetRootPath, Path.Combine("Network", "Certificates", "Wifi"), assetFiles)
            : null;

        string? wifiProfileRelativePath = wifi.IsEnabled
            ? PrepareWifiProfile(wifi, assetRootPath, assetFiles)
            : null;

        FoundryConnectConfigurationDocument configuration = new()
        {
            Capabilities = new ConnectNetworkCapabilitiesSettings
            {
                WifiProvisioned = document.Network.WifiProvisioned
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
                IsEnabled = wifi.IsEnabled && document.Network.WifiProvisioned,
                EnterpriseProfileTemplatePath = wifi.HasEnterpriseProfile
                    ? wifiProfileRelativePath
                    : null,
                CertificatePath = wifiCertificateRelativePath,
                Passphrase = wifi.HasEnterpriseProfile ? null : wifi.Passphrase,
                EnterpriseAuthenticationMode = NetworkAuthenticationMode.UserOnly,
                AllowRuntimeCredentials = false
            }
        };

        return new FoundryConnectProvisioningBundle
        {
            ConfigurationJson = JsonSerializer.Serialize(configuration, ConfigurationJsonDefaults.SerializerOptions),
            AssetFiles = assetFiles
        };
    }

    private static string? PrepareWifiProfile(
        WifiSettings wifi,
        string assetRootPath,
        ICollection<FoundryConnectProvisionedAssetFile> assetFiles)
    {
        if (wifi.HasEnterpriseProfile)
        {
            return CopyOptionalAsset(
                wifi.EnterpriseProfileTemplatePath,
                assetRootPath,
                Path.Combine("Network", "Wifi", "Profiles"),
                assetFiles);
        }

        return null;
    }

    private static void ValidateNetworkSettings(NetworkSettings settings)
    {
        if (settings.Wifi.IsEnabled && !settings.WifiProvisioned)
        {
            throw new InvalidOperationException("Wi-Fi configuration requires Wi-Fi support to be provisioned in the boot image.");
        }

        if (settings.Dot1x.IsEnabled)
        {
            if (string.IsNullOrWhiteSpace(settings.Dot1x.ProfileTemplatePath))
            {
                throw new InvalidOperationException("Wired 802.1X requires a wired profile template.");
            }

            if (settings.Dot1x.RequiresCertificate && string.IsNullOrWhiteSpace(settings.Dot1x.CertificatePath))
            {
                throw new InvalidOperationException("Wired 802.1X certificate trust requires a trusted root CA certificate file.");
            }
        }

        if (!settings.Wifi.IsEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.Wifi.Ssid))
        {
            throw new InvalidOperationException("Wi-Fi configuration requires an SSID.");
        }

        bool isPersonal = string.Equals(settings.Wifi.SecurityType, "WPA2-Personal", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(settings.Wifi.SecurityType, "WPA2/WPA3-Personal", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(settings.Wifi.SecurityType, "WPA3-Personal", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(settings.Wifi.SecurityType, "Personal", StringComparison.OrdinalIgnoreCase);
        bool isEnterprise = settings.Wifi.HasEnterpriseProfile ||
                            string.Equals(settings.Wifi.SecurityType, "WPA2/WPA3-Enterprise", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(settings.Wifi.SecurityType, "WPA2-Enterprise", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(settings.Wifi.SecurityType, "WPA3-Enterprise", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(settings.Wifi.SecurityType, "Enterprise", StringComparison.OrdinalIgnoreCase);

        if (isPersonal)
        {
            int passphraseLength = settings.Wifi.Passphrase?.Trim().Length ?? 0;
            if (passphraseLength is < 8 or > 63)
            {
                throw new InvalidOperationException("WPA2 Personal Wi-Fi requires an 8 to 63 character passphrase.");
            }
        }

        if (isEnterprise)
        {
            if (string.IsNullOrWhiteSpace(settings.Wifi.EnterpriseProfileTemplatePath))
            {
                throw new InvalidOperationException("Enterprise Wi-Fi requires a profile template.");
            }

            if (settings.Wifi.RequiresCertificate && string.IsNullOrWhiteSpace(settings.Wifi.CertificatePath))
            {
                throw new InvalidOperationException("Enterprise Wi-Fi certificate trust requires a trusted root CA certificate file.");
            }
        }
    }

    private static string? CopyOptionalAsset(
        string? sourcePath,
        string assetRootPath,
        string relativeConfigPath,
        ICollection<FoundryConnectProvisionedAssetFile> assetFiles)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return null;
        }

        string fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException($"Provisioning source file was not found: '{fullSourcePath}'.", fullSourcePath);
        }

        string safeFileName = Path.GetFileName(fullSourcePath);
        string destinationFilePath = Path.Combine(assetRootPath, relativeConfigPath, safeFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFilePath)!);
        File.Copy(fullSourcePath, destinationFilePath, overwrite: true);

        string embeddedRelativePath = Path.Combine("Foundry", "Config", relativeConfigPath, safeFileName);
        assetFiles.Add(new FoundryConnectProvisionedAssetFile
        {
            SourcePath = destinationFilePath,
            RelativeDestinationPath = embeddedRelativePath
        });

        return NormalizeEmbeddedRelativePath(Path.Combine(relativeConfigPath, safeFileName));
    }

    private static string NormalizeEmbeddedRelativePath(string relativePath)
    {
        return relativePath.Replace('/', '\\').TrimStart('\\');
    }

    private static void EnsureDirectoryClean(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
    }
}
