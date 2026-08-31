// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Logging;
using Foundry.Utilities.Diagnostics;
using Serilog;
using Serilog.Events;

namespace Foundry.Deploy.Tests;

[Collection(nameof(SerilogCollection))]
public sealed class DeploymentLogServiceTests
{
    [Fact]
    public async Task AppendAsync_WritesThroughGlobalStructuredLogger()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), $"foundry-deploy-log-{Guid.NewGuid():N}");
        string globalLogPath = Path.Combine(rootPath, "global", "FoundryDeploy.log");
        Directory.CreateDirectory(Path.GetDirectoryName(globalLogPath)!);
        Log.Logger = FoundryLogConfiguration.CreateFileLogger(
            globalLogPath,
            "Foundry.Deploy",
            "SESSION01",
            LogEventLevel.Debug,
            retainedFileCountLimit: 2);

        try
        {
            var service = new DeploymentLogService();
            DeploymentLogSession session = service.Initialize(Path.Combine(rootPath, "session"));

            await service.AppendAsync(
                session,
                DeploymentLogLevel.Info,
                "Deployment checkpoint completed.",
                TestContext.Current.CancellationToken);
        }
        finally
        {
            Log.CloseAndFlush();
        }

        string output = await File.ReadAllTextAsync(globalLogPath, TestContext.Current.CancellationToken);
        Assert.Contains("[DeploymentLogService] Deployment checkpoint completed.", output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(rootPath, "session", "Logs", FoundryDeployLogging.LogFileName)));
        Directory.Delete(rootPath, recursive: true);
    }

    [Fact]
    public void PersistLogSnapshot_CopiesAllLogFilesWithoutRecursing()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), $"foundry-deploy-persist-{Guid.NewGuid():N}");
        string sourcePath = Path.Combine(rootPath, "source");
        string targetPath = Path.Combine(rootPath, "target");
        Directory.CreateDirectory(sourcePath);
        File.WriteAllText(Path.Combine(sourcePath, "FoundryDeploy.log"), "deploy");
        File.WriteAllText(Path.Combine(sourcePath, "FoundryConnect.log"), "connect");
        File.WriteAllText(Path.Combine(sourcePath, "ignored.json"), "{}");

        LogPersistenceResult result = FoundryDeployLogging.PersistLogSnapshot(sourcePath, targetPath);

        Assert.Equal(2, result.CopiedFileCount);
        Assert.Equal(0, result.FailedFileCount);
        Assert.Equal("deploy", File.ReadAllText(Path.Combine(targetPath, "FoundryDeploy.log")));
        Assert.Equal("connect", File.ReadAllText(Path.Combine(targetPath, "FoundryConnect.log")));
        Assert.False(File.Exists(Path.Combine(targetPath, "ignored.json")));

        File.WriteAllText(Path.Combine(sourcePath, "FoundryDeploy.log"), "deploy-final");
        LogPersistenceResult refreshedResult = FoundryDeployLogging.PersistLogSnapshot(sourcePath, targetPath);

        Assert.Equal(2, refreshedResult.CopiedFileCount);
        Assert.Equal("deploy-final", File.ReadAllText(Path.Combine(targetPath, "FoundryDeploy.log")));
        Directory.Delete(rootPath, recursive: true);
    }

    [Fact]
    public void PersistCurrentLogs_WhenBootstrapProvidesCacheDirectory_RefreshesCacheSnapshot()
    {
        const string persistenceEnvironmentVariable = "FOUNDRY_DIAGNOSTIC_PERSISTENCE_DIRECTORY";
        string rootPath = Path.Combine(Path.GetTempPath(), $"foundry-deploy-cache-persist-{Guid.NewGuid():N}");
        string sourcePath = Path.Combine(rootPath, "source", FoundryDeployLogging.LogFileName);
        string targetDirectoryPath = Path.Combine(rootPath, "cache", "Logs", "SESSION01");
        string? previousValue = Environment.GetEnvironmentVariable(persistenceEnvironmentVariable);
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        Environment.SetEnvironmentVariable(persistenceEnvironmentVariable, targetDirectoryPath);

        try
        {
            Log.Logger = FoundryDeployLogging.CreateLogger(sourcePath);
            Log.Information("Final deployment outcome.");
            Log.CloseAndFlush();

            LogPersistenceResult result = FoundryDeployLogging.PersistCurrentLogs();

            Assert.True(result.CopiedFileCount >= 1);
            Assert.Contains(
                "Final deployment outcome.",
                File.ReadAllText(Path.Combine(targetDirectoryPath, FoundryDeployLogging.LogFileName)),
                StringComparison.Ordinal);
        }
        finally
        {
            Log.CloseAndFlush();
            Environment.SetEnvironmentVariable(persistenceEnvironmentVariable, previousValue);
            Directory.Delete(rootPath, recursive: true);
        }
    }
}

[CollectionDefinition(nameof(SerilogCollection), DisableParallelization = true)]
public sealed class SerilogCollection;
