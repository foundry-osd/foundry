// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Telemetry;

namespace Foundry.Core.Tests.TestUtilities;

/// <summary>
/// Builds the authoring network configuration behind the <c>winpe-dot1x-wifi*.json</c> fixtures: wired 802.1X with a client
/// certificate and a trusted root, plus personal or enterprise Wi-Fi. Source files are created in a <c>source</c> folder.
/// </summary>
internal static class NetworkFixtureSources
{
    /// <summary>Creates the authoring document and its source files under <paramref name="rootPath"/>\source.</summary>
    public static FoundryConfigurationDocument Create(string rootPath, bool enterpriseWifi)
    {
        WifiSettings wifi = enterpriseWifi
            ? new WifiSettings
            {
                IsEnabled = true,
                Ssid = "Corp WiFi",
                SecurityType = NetworkConfigurationValidator.WifiSecurityEnterprise,
                HasEnterpriseProfile = true,
                EnterpriseProfileTemplate = TestFileReferences.For(CreateSourceFile(
                    rootPath,
                    "wifi.xml",
                    """
                    <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
                      <name>Corp WiFi</name>
                      <MSM>
                        <security>
                          <authEncryption>
                            <authentication>WPA2</authentication>
                          </authEncryption>
                        </security>
                      </MSM>
                    </WLANProfile>
                    """)),
                EnterpriseAuthenticationMode = NetworkAuthenticationMode.MachineOrUser,
                AllowRuntimeCredentials = true,
                RequiresCertificate = true,
                ClientCertificate = TestFileReferences.For(CreateSourceFile(rootPath, "wifi.pfx", "wifi-pfx"))
            }
            : new WifiSettings
            {
                IsEnabled = true,
                Ssid = "Branch WiFi",
                SecurityType = "WPA2/WPA3-Personal"
            };

        return new FoundryConfigurationDocument
        {
            Network = new NetworkSettings
            {
                WifiProvisioned = true,
                RoamWiredDot1xProfileToWindows = true,
                RoamWiredDot1xPrivateKeyMaterialToWindows = true,
                RoamWifiProfileToWindows = true,
                Dot1x = new Dot1xSettings
                {
                    IsEnabled = true,
                    ProfileTemplate = TestFileReferences.For(CreateSourceFile(rootPath, "wired.xml", "<LANProfile />")),
                    AuthenticationMode = NetworkAuthenticationMode.UserOnly,
                    AllowRuntimeCredentials = true,
                    RequiresCertificate = true,
                    ClientCertificate = TestFileReferences.For(CreateSourceFile(rootPath, "wired.pfx", "wired-pfx")),
                    TrustedRootCertificates = [TestFileReferences.For(CreateSourceFile(rootPath, "wired-root.cer", "wired-root"))]
                },
                Wifi = wifi
            },
            Telemetry = new TelemetrySettings { IsEnabled = true, InstallId = "fixture-install" }
        };
    }

    /// <summary>Gets the session passwords of the fixture network.</summary>
    public static NetworkSecrets Secrets(bool enterpriseWifi) => enterpriseWifi
        ? new NetworkSecrets { WiredCertificatePfxPassword = "wired-password", WifiCertificatePfxPassword = "wifi-password" }
        : new NetworkSecrets { WiredCertificatePfxPassword = "wired-password", WifiPassphrase = "personal-passphrase" };

    /// <summary>Resolves references by file name in <paramref name="rootPath"/>\source.</summary>
    public static MediaInputs Inputs(string rootPath, NetworkSecrets? secrets = null) => new()
    {
        ResolveFile = reference => Path.Combine(rootPath, "source", reference.FileName) is var path && File.Exists(path) ? path : null,
        Network = secrets ?? new NetworkSecrets()
    };

    private static string CreateSourceFile(string rootPath, string fileName, string contents)
    {
        string directoryPath = Path.Combine(rootPath, "source");
        Directory.CreateDirectory(directoryPath);
        string path = Path.Combine(directoryPath, fileName);
        File.WriteAllText(path, contents);
        return path;
    }
}
