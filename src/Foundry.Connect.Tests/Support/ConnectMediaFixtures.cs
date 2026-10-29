// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.Runtime;

namespace Foundry.Connect.Tests.Support;

/// <summary>
/// Writes Foundry Connect boot media layouts for loader tests.
/// </summary>
internal static class ConnectMediaFixtures
{
    /// <summary>The deployment password of the protected fixtures.</summary>
    public const string Password = "correct horse battery staple";

    private static readonly JsonSerializerOptions SchemaFiveSerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Writes schema 5 media as released before the deployment key protected network secrets: <c>Config\foundry.connect.config.json</c>
    /// with media-keyed envelopes, and <c>Config\Secrets\media-secrets.key</c> when at least one envelope was written.
    /// </summary>
    /// <remarks>
    /// A Wi-Fi certificate path makes the Wi-Fi profile enterprise; otherwise a passphrase makes it personal. The document keeps the
    /// fields schema 5 always wrote (authentication modes, <c>requiresCertificate</c>, <c>internetProbe</c>).
    /// </remarks>
    /// <returns>The configuration file path.</returns>
    public static string WriteSchemaFive(
        TemporaryDirectory directory,
        string? wifiPassphrase = null,
        string? wiredCertificatePath = null,
        string? wiredPfxPassword = null,
        string? wifiCertificatePath = null,
        string? wifiPfxPassword = null)
    {
        ArgumentNullException.ThrowIfNull(directory);
        byte[] mediaKey = MediaSecretEnvelopeProtector.GenerateMediaKey();
        try
        {
            bool hasEnvelope = false;
            SecretEnvelope? Encrypt(string? value)
            {
                if (value is null)
                {
                    return null;
                }

                hasEnvelope = true;
                return MediaSecretEnvelopeProtector.EncryptString(value, mediaKey);
            }

            bool isWifiEnterprise = wifiCertificatePath is not null;
            bool isWifiEnabled = isWifiEnterprise || wifiPassphrase is not null;
            var document = new FoundryConnectConfigurationDocument
            {
                SchemaVersion = 5,
                Capabilities = new ConnectNetworkCapabilitiesSettings { WifiProvisioned = isWifiEnabled },
                Dot1x = wiredCertificatePath is null
                    ? new ConnectDot1xSettings()
                    : new ConnectDot1xSettings
                    {
                        IsEnabled = true,
                        ProfileTemplatePath = @"Network\Wired\Profiles\wired.xml",
                        RequiresCertificate = true,
                        CertificatePath = wiredCertificatePath,
                        CertificatePfxPasswordSecret = Encrypt(wiredPfxPassword)
                    },
                Wifi = !isWifiEnabled
                    ? new ConnectWifiSettings()
                    : new ConnectWifiSettings
                    {
                        IsEnabled = true,
                        Ssid = "Corp WiFi",
                        SecurityType = isWifiEnterprise ? "WPA2/WPA3-Enterprise" : "WPA2/WPA3-Personal",
                        PassphraseSecret = isWifiEnterprise ? null : Encrypt(wifiPassphrase),
                        HasEnterpriseProfile = isWifiEnterprise,
                        EnterpriseProfileTemplatePath = isWifiEnterprise ? @"Network\Wifi\Profiles\wifi.xml" : null,
                        RequiresCertificate = isWifiEnterprise,
                        CertificatePath = wifiCertificatePath,
                        CertificatePfxPasswordSecret = isWifiEnterprise ? Encrypt(wifiPfxPassword) : null
                    }
            };

            string configurationPath = directory.CreateFile(
                Path.Combine("Config", "foundry.connect.config.json"),
                JsonSerializer.Serialize(document, SchemaFiveSerializerOptions));
            if (hasEnvelope)
            {
                directory.CreateFile(Path.Combine("Config", WinPeWorkspaceLayout.MediaSecretsKeyRelativePath), mediaKey);
            }

            return configurationPath;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(mediaKey);
        }
    }
}
