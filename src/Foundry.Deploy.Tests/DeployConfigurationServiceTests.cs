using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class DeployConfigurationServiceTests
{
    [Fact]
    public void LoadOptional_WhenSchemaIsOlderThanCurrent_RecommendsBootMediaUpdate()
    {
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = CreateJsonFile(
            tempDirectory.Path,
            "foundry.deploy.config.json",
            $$"""
            {
              "schemaVersion": {{FoundryDeployConfigurationDocument.CurrentSchemaVersion - 1}}
            }
            """);

        var service = new DeployConfigurationService(
            NullLogger<DeployConfigurationService>.Instance,
            configurationPath);

        DeployConfigurationLoadResult result = service.LoadOptional();

        Assert.True(result.Exists);
        Assert.NotNull(result.Document);
        Assert.Equal(FoundryDeployConfigurationDocument.CurrentSchemaVersion - 1, result.Document.SchemaVersion);
        Assert.True(result.IsBootMediaUpdateRecommended);
    }

    [Fact]
    public void LoadOptional_WhenSchemaIsCurrent_DoesNotRecommendBootMediaUpdate()
    {
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = CreateJsonFile(
            tempDirectory.Path,
            "foundry.deploy.config.json",
            $$"""
            {
              "schemaVersion": {{FoundryDeployConfigurationDocument.CurrentSchemaVersion}}
            }
            """);

        var service = new DeployConfigurationService(
            NullLogger<DeployConfigurationService>.Instance,
            configurationPath);

        DeployConfigurationLoadResult result = service.LoadOptional();

        Assert.True(result.Exists);
        Assert.NotNull(result.Document);
        Assert.Equal(FoundryDeployConfigurationDocument.CurrentSchemaVersion, result.Document.SchemaVersion);
        Assert.False(result.IsBootMediaUpdateRecommended);
    }

    [Fact]
    public void LoadOptional_WhenConfigurationContainsNetworkProfileRoaming_PreservesOptIn()
    {
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = CreateJsonFile(
            tempDirectory.Path,
            "foundry.deploy.config.json",
            $$"""
            {
              "schemaVersion": {{FoundryDeployConfigurationDocument.CurrentSchemaVersion}},
              "network": {
                "profileRoaming": {
                  "isEnabled": true,
                  "includePrivateKeyMaterial": true
                }
              }
            }
            """);

        var service = new DeployConfigurationService(
            NullLogger<DeployConfigurationService>.Instance,
            configurationPath);

        DeployConfigurationLoadResult result = service.LoadOptional();

        Assert.NotNull(result.Document);
        Assert.True(result.Document.Network.ProfileRoaming.IsEnabled);
        Assert.True(result.Document.Network.ProfileRoaming.IncludePrivateKeyMaterial);
    }

    private static string CreateJsonFile(string directoryPath, string fileName, string contents)
    {
        string filePath = Path.Combine(directoryPath, fileName);
        File.WriteAllText(filePath, contents);
        return filePath;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Foundry.Deploy.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
