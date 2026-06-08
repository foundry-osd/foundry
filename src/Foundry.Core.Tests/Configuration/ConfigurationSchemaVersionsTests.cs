using System.Reflection;
using System.Runtime.CompilerServices;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;

namespace Foundry.Core.Tests.Configuration;

public sealed class ConfigurationSchemaVersionsTests
{
    [Fact]
    public void CurrentVersions_MatchConfigurationDocumentContracts()
    {
        Assert.Equal(FoundryConfigurationDocument.CurrentSchemaVersion, ConfigurationSchemaVersions.FoundryCurrent);
        Assert.Equal(FoundryConnectConfigurationDocument.CurrentSchemaVersion, ConfigurationSchemaVersions.ConnectCurrent);
        Assert.Equal(FoundryDeployConfigurationDocument.CurrentSchemaVersion, ConfigurationSchemaVersions.DeployCurrent);
    }

    [Fact]
    public void MinimumRecommendedVersions_OnlyExistForRuntimeContracts()
    {
        Assert.Equal(ConfigurationSchemaVersions.ConnectCurrent, ConfigurationSchemaVersions.ConnectMinimumRecommended);
        Assert.Equal(ConfigurationSchemaVersions.DeployCurrent, ConfigurationSchemaVersions.DeployMinimumRecommended);
        FieldInfo[] authoringMinimumFields = typeof(ConfigurationSchemaVersions)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static field =>
                field.Name.StartsWith("Foundry", StringComparison.Ordinal) &&
                field.Name.Contains("MinimumRecommended", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(authoringMinimumFields);
    }

    [Fact]
    public void MinimumRecommendedVersions_AreDeclaredIndependentlyFromCurrentVersions()
    {
        string source = File.ReadAllText(GetConfigurationSchemaVersionsSourcePath());

        Assert.DoesNotContain("ConnectMinimumRecommended = ConnectCurrent", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DeployMinimumRecommended = DeployCurrent", source, StringComparison.Ordinal);
    }

    [Fact]
    public void IsBootMediaUpdateRecommended_UsesMinimumRecommendedSchemaVersion()
    {
        Assert.False(ConfigurationSchemaVersions.IsBootMediaUpdateRecommended(4, 3));
        Assert.False(ConfigurationSchemaVersions.IsBootMediaUpdateRecommended(3, 3));
        Assert.True(ConfigurationSchemaVersions.IsBootMediaUpdateRecommended(2, 3));
    }

    private static string GetConfigurationSchemaVersionsSourcePath([CallerFilePath] string testFilePath = "")
    {
        return Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(testFilePath)!,
            "..",
            "..",
            "Foundry.Core",
            "Models",
            "Configuration",
            "ConfigurationSchemaVersions.cs"));
    }
}
