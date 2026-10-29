// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class ConnectCertificateSetTests
{
    private static FoundryConnectConfigurationDocument Fixture(string name) =>
        JsonSerializer.Deserialize<FoundryConnectConfigurationDocument>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Configuration", "Fixtures", name)), ConfigurationJsonDefaults.SerializerOptions)!;

    [Fact]
    public void From_SchemaFivePfx_IsTheClientCertificateWithItsPassword()
    {
        FoundryConnectConfigurationDocument document = Fixture("connect-schema5-dot1x-wifi-enterprise.json");

        ConnectCertificateSet wired = ConnectCertificateSet.From(document.Dot1x);
        ConnectCertificateSet wifi = ConnectCertificateSet.From(document.Wifi);

        Assert.Equal(@"Network\Certificates\Wired\wired.pfx", wired.ClientCertificatePath);
        Assert.False(wired.IsClientCertificateContentProtected);
        Assert.Equal("media", wired.ClientCertificatePasswordSecret!.KeyId);
        Assert.Empty(wired.TrustedRootCertificatePaths);
        Assert.Equal(@"Network\Certificates\Wifi\wifi.pfx", wifi.ClientCertificatePath);
    }

    [Fact]
    public void From_SchemaFiveCer_IsASingleTrustedRoot()
    {
        ConnectCertificateSet set = ConnectCertificateSet.From(new ConnectDot1xSettings
        {
            CertificatePath = @"Network\Certificates\Wired\root.cer",
            CertificatePfxPasswordSecret = new SecretEnvelope { KeyId = "media" }
        });

        Assert.Null(set.ClientCertificatePath);
        Assert.Null(set.ClientCertificatePasswordSecret);
        Assert.Equal([@"Network\Certificates\Wired\root.cer"], set.TrustedRootCertificatePaths);
    }

    [Fact]
    public void From_SchemaSixShape_WinsOverLegacyFields()
    {
        ConnectCertificateSet set = ConnectCertificateSet.From(new ConnectWifiSettings
        {
            CertificatePath = @"Network\old.pfx",
            ClientCertificate = new ConnectClientCertificateSettings { Path = @"Network\client.pfx.encrypted", IsContentProtected = true },
            TrustedRootCertificates = [@"Network\root1.cer", " ", @"Network\root2.crt"]
        });

        Assert.Equal(@"Network\client.pfx.encrypted", set.ClientCertificatePath);
        Assert.True(set.IsClientCertificateContentProtected);
        Assert.Equal([@"Network\root1.cer", @"Network\root2.crt"], set.TrustedRootCertificatePaths);
    }

    [Fact]
    public void From_NoCertificate_IsEmpty() => Assert.Equal(ConnectCertificateSet.Empty, ConnectCertificateSet.From(new ConnectDot1xSettings()));

    [Fact]
    public void NewMembers_AreOmittedUntilSet()
    {
        string json = JsonSerializer.Serialize(new FoundryConnectConfigurationDocument(), ConfigurationJsonDefaults.SerializerOptions);

        foreach (string name in new[] { "clientCertificate", "trustedRootCertificates", "isEnterpriseProfileTemplateProtected", "protection" })
        {
            Assert.DoesNotContain($"\"{name}\"", json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SchemaSixMembers_RoundTripThroughJson()
    {
        var document = new FoundryConnectConfigurationDocument
        {
            Dot1x = new ConnectDot1xSettings
            {
                ClientCertificate = new ConnectClientCertificateSettings
                {
                    Path = @"Network\Certificates\Wired\client.pfx.encrypted",
                    IsContentProtected = true,
                    PasswordSecret = new SecretEnvelope { Kind = "encrypted", KeyId = "deployment" }
                },
                TrustedRootCertificates = [@"Network\Certificates\Wired\root.cer"]
            },
            Wifi = new ConnectWifiSettings
            {
                IsEnterpriseProfileTemplateProtected = true,
                TrustedRootCertificates = [@"Network\Certificates\Wifi\root1.cer", @"Network\Certificates\Wifi\root2.crt"]
            },
            Protection = new DeployProtectionSettings { IsEnabled = true }
        };

        string json = JsonSerializer.Serialize(document, ConfigurationJsonDefaults.SerializerOptions);
        FoundryConnectConfigurationDocument roundTripped = JsonSerializer.Deserialize<FoundryConnectConfigurationDocument>(json, ConfigurationJsonDefaults.SerializerOptions)!;

        Assert.Equal(document.Dot1x.ClientCertificate, roundTripped.Dot1x.ClientCertificate);
        Assert.Equal(document.Dot1x.TrustedRootCertificates, roundTripped.Dot1x.TrustedRootCertificates);
        Assert.Null(roundTripped.Wifi.ClientCertificate);
        Assert.True(roundTripped.Wifi.IsEnterpriseProfileTemplateProtected);
        Assert.Equal(document.Wifi.TrustedRootCertificates, roundTripped.Wifi.TrustedRootCertificates);
        Assert.True(roundTripped.Protection!.IsEnabled);
    }
}
