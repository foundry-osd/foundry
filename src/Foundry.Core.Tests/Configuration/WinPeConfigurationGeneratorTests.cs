// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.Text.Json.Nodes;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.WinPe;
using Foundry.Core.Tests.TestUtilities;
using Foundry.Telemetry;

namespace Foundry.Core.Tests.Configuration;

public sealed class WinPeConfigurationGeneratorTests
{
    private const string Password = "correct horse battery staple";

    [Fact]
    public void CreateBundle_WritesTheSharedSectionsOnceAtTheRoot()
    {
        using var directory = new TemporaryDirectory();
        using DeploymentMediaProtectionMaterial protection = DeploymentMediaProtectionService.CreateUnprotected();
        var telemetry = new TelemetrySettings { InstallId = "install-id", RuntimePayloadSource = TelemetryRuntimePayloadSources.Debug };

        WinPeConfigurationBundle bundle = new WinPeConfigurationGenerator().CreateBundle(
            new FoundryConfigurationDocument { Telemetry = telemetry }, MediaInputs.None, directory.Path, protection, authoringVersion: "26.11.1.1");

        JsonObject root = JsonNode.Parse(bundle.ConfigurationJson)!.AsObject();
        Assert.Equal(new[] { "schemaVersion", "authoringVersion", "telemetry", "protection", "network", "connect", "deploy" }, root.Select(property => property.Key));
        Assert.Equal(ConfigurationSchemaVersions.WinPeCurrent, root["schemaVersion"]!.GetValue<int>());
        Assert.Equal(1, ConfigurationSchemaVersions.WinPeCurrent);
        Assert.Equal("26.11.1.1", root["authoringVersion"]!.GetValue<string>());
        Assert.Equal("install-id", root["telemetry"]!["installId"]!.GetValue<string>());
        Assert.Equal(TelemetryRuntimePayloadSources.Debug, root["telemetry"]!["runtimePayloadSource"]!.GetValue<string>());
        Assert.False(root["protection"]!["isEnabled"]!.GetValue<bool>());
        Assert.Equal(new[] { "profileRoaming", "artifactRootPath" }, root["network"]!.AsObject().Select(property => property.Key));
        Assert.Equal(@"X:\Foundry\Runtime\NetworkProfileRoaming", root["network"]!["artifactRootPath"]!.GetValue<string>());
        Assert.Equal(new[] { "capabilities", "dot1x", "wifi" }, root["connect"]!.AsObject().Select(property => property.Key));
        JsonObject deploy = root["deploy"]!.AsObject();
        Assert.All(["schemaVersion", "authoringVersion", "telemetry", "protection", "network"], name => Assert.False(deploy.ContainsKey(name)));
        Assert.True(deploy.ContainsKey("completion"));
        Assert.True(deploy.ContainsKey("networkCache"));
        Assert.Equal(telemetry, bundle.Configuration.Telemetry);
        Assert.Empty(bundle.ConnectAssetFiles);
    }

    [Fact]
    public void CreateBundle_MapsRoamingAndPerModeValuesIntoTheirSections()
    {
        using var directory = new TemporaryDirectory();
        using DeploymentMediaProtectionMaterial protection = DeploymentMediaProtectionService.CreateUnprotected();
        var document = new FoundryConfigurationDocument
        {
            Network = new NetworkSettings
            {
                RoamWiredDot1xProfileToWindows = true,
                RoamWiredDot1xPrivateKeyMaterialToWindows = true,
                RoamWifiProfileToWindows = false
            },
            Localization = new LocalizationSettings { DefaultTimeZoneId = "Romance Standard Time" },
            General = new GeneralSettings { AutomaticRebootEnabled = true, AutomaticRebootDelaySeconds = 30 }
        };

        FoundryWinPeConfigurationDocument configuration = new WinPeConfigurationGenerator()
            .CreateBundle(document, MediaInputs.None, directory.Path, protection).Configuration;

        Assert.True(configuration.Network.ProfileRoaming.WiredDot1x.IsEnabled);
        Assert.True(configuration.Network.ProfileRoaming.WiredDot1x.IncludePrivateKeyMaterial);
        Assert.False(configuration.Network.ProfileRoaming.Wifi.IsEnabled);
        Assert.Equal("Romance Standard Time", configuration.Deploy.Localization.DefaultTimeZoneId);
        Assert.True(configuration.Deploy.Completion.AutomaticRebootEnabled);
        Assert.False(configuration.Connect.Capabilities.WifiProvisioned);
    }

    [Fact]
    public void CreateBundle_ProtectedMedia_KeysNetworkSecretsAndDomainCredentialsToTheDeploymentKey()
    {
        using var directory = new TemporaryDirectory();
        using DeploymentMediaProtectionMaterial protection = DeploymentMediaProtectionService.CreateProtected(Password);
        using var domainSecrets = new DomainJoinSecretState();
        domainSecrets.SetPassword("CORP\\join", "domain-password");
        FoundryConfigurationDocument document = NetworkFixtureSources.Create(directory.Path, enterpriseWifi: false) with
        {
            DomainJoin = new DomainJoinSettings
            {
                IsEnabled = true,
                Mode = DomainJoinMode.Automatic,
                SharedAccountName = "CORP\\join",
                DefaultDomainId = "corp",
                Domains = [new() { Id = "corp", DomainName = "corp.test" }]
            }
        };

        WinPeConfigurationBundle bundle = new WinPeConfigurationGenerator().CreateBundle(
            document,
            NetworkFixtureSources.Inputs(directory.Path, NetworkFixtureSources.Secrets(enterpriseWifi: false)),
            directory.Path,
            protection,
            domainJoinSecretState: domainSecrets);

        FoundryWinPeConfigurationDocument configuration = bundle.Configuration;
        Assert.Equal(protection.Settings, configuration.Protection);
        Assert.NotNull(configuration.Protection.ProtectedDeploymentKey);
        SecretEnvelope passphrase = configuration.Connect.Wifi.PassphraseSecret!;
        SecretEnvelope pfxPassword = configuration.Connect.Dot1x.ClientCertificate!.PasswordSecret!;
        SecretEnvelope credentials = Assert.Single(configuration.Deploy.DomainJoin.Domains).EncryptedCredentials!;
        Assert.All(new[] { passphrase, pfxPassword, credentials }, envelope => Assert.Equal(MediaSecretEnvelopeProtector.DeploymentKeyId, envelope.KeyId));
        Assert.Equal("personal-passphrase", MediaSecretEnvelopeProtector.DecryptString(passphrase, protection.DeploymentKey, MediaSecretEnvelopeProtector.DeploymentKeyId));
        Assert.Equal("wired-password", MediaSecretEnvelopeProtector.DecryptString(pfxPassword, protection.DeploymentKey, MediaSecretEnvelopeProtector.DeploymentKeyId));
        Assert.DoesNotContain("personal-passphrase", bundle.ConfigurationJson, StringComparison.Ordinal);
        Assert.DoesNotContain("domain-password", bundle.ConfigurationJson, StringComparison.Ordinal);
        Assert.False(MediaSecretEnvelopeProtector.HasEncryptedSecrets(bundle.ConfigurationJson));
        Assert.Contains(bundle.ConnectAssetFiles, asset => asset.RelativeDestinationPath == @"Foundry\Config\Network\Certificates\Wired\wired.pfx.encrypted");
    }

    [Fact]
    public void CreateBundle_NetworkSecretOnUnprotectedMedia_IsRefused()
    {
        using var directory = new TemporaryDirectory();
        using DeploymentMediaProtectionMaterial unprotected = DeploymentMediaProtectionService.CreateUnprotected();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new WinPeConfigurationGenerator().CreateBundle(
            NetworkFixtureSources.Create(directory.Path, enterpriseWifi: false),
            NetworkFixtureSources.Inputs(directory.Path, NetworkFixtureSources.Secrets(enterpriseWifi: false)),
            directory.Path,
            unprotected));

        Assert.Contains("MediaProtectionRequired", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("winpe-dot1x-wifi.json", false)]
    [InlineData("winpe-dot1x-wifi-enterprise.json", true)]
    public void CreateBundle_SharedAndConnectSections_MatchTheFixture(string fixtureName, bool enterpriseWifi)
    {
        using var directory = new TemporaryDirectory();
        using DeploymentMediaProtectionMaterial protection = DeploymentMediaProtectionService.CreateProtected(Password);

        WinPeConfigurationBundle bundle = new WinPeConfigurationGenerator().CreateBundle(
            NetworkFixtureSources.Create(directory.Path, enterpriseWifi),
            NetworkFixtureSources.Inputs(directory.Path, NetworkFixtureSources.Secrets(enterpriseWifi)),
            directory.Path,
            protection,
            authoringVersion: "26.10.18.1");

        // The deploy section has its own generator tests; the fixture locks the shared and Connect contract.
        JsonObject root = JsonNode.Parse(bundle.ConfigurationJson)!.AsObject();
        root.Remove("deploy");
        string expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Configuration", "Fixtures", fixtureName));
        Assert.Equal(NormalizeRuntimeJson(expected), NormalizeRuntimeJson(root.ToJsonString(ConfigurationJsonDefaults.SerializerOptions)));
    }

    [Fact]
    public void Serialize_OmitsTheRetiredConnectFields()
    {
        string json = WinPeConfigurationGenerator.Serialize(new FoundryWinPeConfigurationDocument());

        Assert.All(
            ["allowRuntimeCredentials", "authenticationMode", "enterpriseAuthenticationMode", "requiresCertificate", "internetProbe"],
            name => Assert.DoesNotContain($"\"{name}\"", json, StringComparison.Ordinal));
    }

    // Envelopes and the password salt are random per generation; every other byte must match the fixture.
    private static string NormalizeRuntimeJson(string json) =>
        System.Text.RegularExpressions.Regex.Replace(
            json.ReplaceLineEndings("\n"),
            "\"(nonce|tag|ciphertext|salt)\": \"[^\"]*\"",
            "\"$1\": \"<random>\"");
}
