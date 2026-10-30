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
    public void From_SchemaFiveP12_IsTheClientCertificate()
    {
        var password = new SecretEnvelope { KeyId = "media" };

        ConnectCertificateSet set = ConnectCertificateSet.From(new ConnectDot1xSettings
        {
            CertificatePath = @"Network\Certificates\Wired\client.P12",
            CertificatePfxPasswordSecret = password
        });

        Assert.Equal(@"Network\Certificates\Wired\client.P12", set.ClientCertificatePath);
        Assert.Same(password, set.ClientCertificatePasswordSecret);
        Assert.Empty(set.TrustedRootCertificatePaths);
    }

    [Fact]
    public void From_TrustedRootsOnly_DropsTheLegacyPfxAndItsPassword()
    {
        ConnectCertificateSet set = ConnectCertificateSet.From(new ConnectDot1xSettings
        {
            CertificatePath = @"Network\Certificates\Wired\old.pfx",
            CertificatePfxPasswordSecret = new SecretEnvelope { KeyId = "media" },
            TrustedRootCertificates = [@"Network\Certificates\Wired\root.cer"]
        });

        Assert.Null(set.ClientCertificatePath);
        Assert.Null(set.ClientCertificatePasswordSecret);
        Assert.Equal([@"Network\Certificates\Wired\root.cer"], set.TrustedRootCertificatePaths);
    }

    [Fact]
    public void From_ClientCertificateWithABlankPath_FallsBackToTheLegacyPath()
    {
        ConnectCertificateSet set = ConnectCertificateSet.From(new ConnectWifiSettings
        {
            CertificatePath = @"Network\Certificates\Wifi\old.pfx",
            ClientCertificate = new ConnectClientCertificateSettings { Path = " ", IsContentProtected = true },
            TrustedRootCertificates = [" "]
        });

        Assert.Equal(@"Network\Certificates\Wifi\old.pfx", set.ClientCertificatePath);
        Assert.False(set.IsClientCertificateContentProtected);
        Assert.Empty(set.TrustedRootCertificatePaths);
    }

    [Fact]
    public void SchemaSixMembers_AreWrittenWithTheirContractNames()
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
            Wifi = new ConnectWifiSettings { IsEnterpriseProfileTemplateProtected = true },
            Protection = new DeployProtectionSettings { IsEnabled = true }
        };

        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(document, ConfigurationJsonDefaults.SerializerOptions));
        JsonElement root = json.RootElement;
        JsonElement client = root.GetProperty("dot1x").GetProperty("clientCertificate");

        Assert.Equal(@"Network\Certificates\Wired\client.pfx.encrypted", client.GetProperty("path").GetString());
        Assert.True(client.GetProperty("isContentProtected").GetBoolean());
        Assert.Equal("deployment", client.GetProperty("passwordSecret").GetProperty("keyId").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("dot1x").GetProperty("trustedRootCertificates").ValueKind);
        Assert.True(root.GetProperty("wifi").GetProperty("isEnterpriseProfileTemplateProtected").GetBoolean());
        Assert.True(root.GetProperty("protection").GetProperty("isEnabled").GetBoolean());
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
