// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class DeploymentProtectionContractTests
{
    [Fact]
    public void FoundryConfiguration_RoundTripPersistsOnlyProtectionEnabledState()
    {
        var service = new FoundryConfigurationService();
        var document = new FoundryConfigurationDocument
        {
            General = new GeneralSettings
            {
                DeploymentProtection = new DeploymentProtectionSettings { IsEnabled = true }
            }
        };

        string json = service.Serialize(document);
        FoundryConfigurationDocument loaded = service.Deserialize(json);

        Assert.True(loaded.General.DeploymentProtection.IsEnabled);
        Assert.Contains("\"deploymentProtection\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("confirmation", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingProtectionProperties_DefaultToDisabled()
    {
        var authoringService = new FoundryConfigurationService();

        FoundryConfigurationDocument authoring = authoringService.Deserialize("{}");
        FoundryDeployConfigurationDocument runtime = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(
            "{}",
            ConfigurationJsonDefaults.SerializerOptions)!;

        Assert.False(authoring.General.DeploymentProtection.IsEnabled);
        Assert.False(runtime.Protection.IsEnabled);
    }

    [Fact]
    public void DeployGenerator_IncludesProtectionMetadataWithoutPassword()
    {
        var generator = new DeployConfigurationGenerator();
        var protection = new DeployProtectionSettings
        {
            IsEnabled = true,
            KeyDerivationAlgorithm = "pbkdf2-sha256",
            Iterations = 600_000,
            Salt = "c2FsdA",
            ProtectedDeploymentKey = new SecretEnvelope
            {
                Kind = "encrypted",
                Algorithm = "aes-gcm-v1",
                KeyId = "deployment-password",
                Nonce = "bm9uY2U",
                Tag = "dGFn",
                Ciphertext = "Y2lwaGVydGV4dA"
            }
        };

        FoundryDeployConfigurationDocument result = generator.Generate(
            new FoundryConfigurationDocument(),
            deploymentSecretsKey: null,
            protectionSettings: protection);
        string json = generator.Serialize(result);

        Assert.Equal(protection, result.Protection);
        Assert.Contains("\"keyDerivationAlgorithm\": \"pbkdf2-sha256\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"password\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"confirmation\":", json, StringComparison.OrdinalIgnoreCase);
    }
}
