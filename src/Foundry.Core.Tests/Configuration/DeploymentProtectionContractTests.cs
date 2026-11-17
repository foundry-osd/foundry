// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.WinPe;

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
        Assert.DoesNotContain("\"password\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("confirmation", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingProtectionProperties_DefaultToDisabled()
    {
        var authoringService = new FoundryConfigurationService();

        FoundryConfigurationDocument authoring = authoringService.Deserialize("{}");
        FoundryWinPeConfigurationDocument runtime = JsonSerializer.Deserialize<FoundryWinPeConfigurationDocument>(
            "{}",
            ConfigurationJsonDefaults.SerializerOptions)!;

        Assert.False(authoring.General.DeploymentProtection.IsEnabled);
        Assert.False(runtime.Protection.IsEnabled);
    }

    [Fact]
    public void WinPeGenerator_WritesTheMediaProtectionBlockWithoutPassword()
    {
        const string password = "correct horse battery staple";
        string staging = Path.Combine(Path.GetTempPath(), "Foundry.Core.Tests", Guid.NewGuid().ToString("N"));
        using DeploymentMediaProtectionMaterial protection = DeploymentMediaProtectionService.CreateProtected(password);
        try
        {
            WinPeConfigurationBundle bundle = new WinPeConfigurationGenerator().CreateBundle(
                new FoundryConfigurationDocument(), MediaInputs.None, staging, protection);

            Assert.Equal(protection.Settings, bundle.Configuration.Protection);
            Assert.Contains("\"keyDerivationAlgorithm\": \"pbkdf2-sha256\"", bundle.ConfigurationJson, StringComparison.Ordinal);
            Assert.DoesNotContain(password, bundle.ConfigurationJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"password\":", bundle.ConfigurationJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"confirmation\":", bundle.ConfigurationJson, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }
}
