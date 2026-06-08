using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class DeployConfigurationServiceTests
{
    [Fact]
    public void LoadOptional_WhenSchemaIsOlderThanMinimumRecommended_RecommendsBootMediaUpdate()
    {
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = CreateJsonFile(
            tempDirectory.Path,
            "foundry.deploy.config.json",
            $$"""
            {
              "schemaVersion": {{Foundry.Core.Models.Configuration.ConfigurationSchemaVersions.DeployMinimumRecommended - 1}}
            }
            """);

        var logger = new RecordingLogger<DeployConfigurationService>();
        var service = new DeployConfigurationService(logger, configurationPath);

        DeployConfigurationLoadResult result = service.LoadOptional();

        Assert.True(result.IsBootMediaUpdateRecommended);
        Assert.Contains(
            logger.Entries,
            entry =>
                entry.LogLevel == LogLevel.Warning &&
                entry.Message.Contains("minimum recommended schema version", StringComparison.Ordinal) &&
                entry.Message.Contains(Foundry.Core.Models.Configuration.ConfigurationSchemaVersions.DeployMinimumRecommended.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public void LoadOptional_WhenSchemaMatchesMinimumRecommended_DoesNotRecommendBootMediaUpdate()
    {
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = CreateJsonFile(
            tempDirectory.Path,
            "foundry.deploy.config.json",
            $$"""
            {
              "schemaVersion": {{Foundry.Core.Models.Configuration.ConfigurationSchemaVersions.DeployMinimumRecommended}}
            }
            """);

        var logger = new RecordingLogger<DeployConfigurationService>();
        var service = new DeployConfigurationService(logger, configurationPath);

        DeployConfigurationLoadResult result = service.LoadOptional();

        Assert.False(result.IsBootMediaUpdateRecommended);
        Assert.DoesNotContain(logger.Entries, entry => entry.LogLevel == LogLevel.Warning);
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

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(LogLevel LogLevel, string Message);
}
