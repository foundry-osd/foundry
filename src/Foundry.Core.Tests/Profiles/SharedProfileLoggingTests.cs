// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Profiles;
using Foundry.Core.Tests.TestUtilities;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Foundry.Core.Tests.Profiles;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProfileLoggingCollection
{
    public const string Name = "ProfileLogging";
}

[Collection(ProfileLoggingCollection.Name)]
public sealed class SharedProfileLoggingTests : IDisposable
{
    private readonly ILogger previousLogger = Log.Logger;
    private readonly CapturingSink sink = new();
    private readonly Logger logger;

    public SharedProfileLoggingTests()
    {
        logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
        Log.Logger = logger;
    }

    [Fact]
    public async Task Initialize_NonemptyFolderLogsActionableStatusWithoutDirectoryContents()
    {
        using var workspace = new TemporaryDirectory();
        const string confidentialFileName = "confidential-customer-file.txt";
        File.WriteAllText(Path.Combine(workspace.Path, confidentialFileName), "Confidential contents");
        using var repository = new SharedProfileRepository(workspace.Path, Guid.NewGuid(), Guid.NewGuid(), 1, new byte[32]);

        SharedProfileRepositoryResult result = await repository.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SharedProfileRepositoryStatus.FolderNotEmpty, result.Status);
        LogEvent failure = Assert.Single(sink.Events, entry => entry.Level == LogEventLevel.Warning);
        Assert.NotNull(failure.Exception);
        Assert.Equal("Initialize", Assert.IsType<ScalarValue>(failure.Properties["ProfileOperation"]).Value);
        Assert.Contains("FolderNotEmpty", failure.RenderMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain(confidentialFileName, failure.RenderMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain("Confidential contents", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_MissingStoragePreservesNativeExceptionAndClassification()
    {
        using var workspace = new TemporaryDirectory();
        using var repository = new SharedProfileRepository(Path.Combine(workspace.Path, "missing"),
            Guid.NewGuid(), Guid.NewGuid(), 1, new byte[32]);

        SharedProfileRepositoryResult result = await repository.LoadAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(SharedProfileRepositoryStatus.Unavailable, result.Status);
        LogEvent failure = Assert.Single(sink.Events, entry => entry.Level == LogEventLevel.Warning);
        Assert.IsAssignableFrom<IOException>(failure.Exception);
        Assert.Equal("Load", Assert.IsType<ScalarValue>(failure.Properties["ProfileOperation"]).Value);
        Assert.Contains("Unavailable", failure.RenderMessage(), StringComparison.Ordinal);
        Assert.Contains(nameof(SharedProfileRepository), Assert.IsType<ScalarValue>(failure.Properties["SourceContext"]).Value?.ToString());
    }

    [Fact]
    public async Task CaptureAsync_OversizedDriversLogValidationWarningWithoutExceptionOrSourcePath()
    {
        using var workspace = new TemporaryDirectory();
        string drivers = Path.Combine(workspace.Path, "confidential-drivers");
        Directory.CreateDirectory(drivers);
        using (FileStream source = File.Create(Path.Combine(drivers, "large.sys"))) source.SetLength(2_147_483_649);
        using var secrets = new OobeAccountSecretState();
        var document = new FoundryConfigurationDocument { General = new() { CustomDriverDirectoryPath = drivers } };

        await Assert.ThrowsAsync<CustomDriverSizeLimitException>(() => DeploymentBuildSnapshot.CaptureAsync(document, secrets, [], Path.Combine(workspace.Path, "snapshots"), TestContext.Current.CancellationToken));

        LogEvent failure = Assert.Single(sink.Events, entry => entry.Level >= LogEventLevel.Warning);
        Assert.Equal(LogEventLevel.Warning, failure.Level);
        Assert.Null(failure.Exception);
        Assert.Equal(2_147_483_649L, Assert.IsType<ScalarValue>(failure.Properties["ActualBytes"]).Value);
        Assert.Equal(2_147_483_648L, Assert.IsType<ScalarValue>(failure.Properties["LimitBytes"]).Value);
        Assert.DoesNotContain(drivers, failure.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CaptureAsync_DriverGrowthAfterValidationIsRejectedAndPrivateFilesAreRemoved()
    {
        using var workspace = new TemporaryDirectory();
        string drivers = Path.Combine(workspace.Path, "drivers");
        string snapshots = Path.Combine(workspace.Path, "snapshots");
        Directory.CreateDirectory(drivers);
        string sourcePath = Path.Combine(drivers, "growing.sys");
        File.WriteAllBytes(sourcePath, [1]);
        sink.OnEmit = entry =>
        {
            if (!entry.MessageTemplate.Text.StartsWith("Profile dependencies captured.", StringComparison.Ordinal)) return;
            using FileStream source = File.OpenWrite(sourcePath);
            source.SetLength(2_147_483_649);
        };
        using var secrets = new OobeAccountSecretState();
        var document = new FoundryConfigurationDocument { General = new() { CustomDriverDirectoryPath = drivers } };

        CustomDriverSizeLimitException exception = await Assert.ThrowsAsync<CustomDriverSizeLimitException>(() => DeploymentBuildSnapshot.CaptureAsync(document, secrets, [], snapshots, TestContext.Current.CancellationToken));

        Assert.Equal(2_147_483_649L, exception.ActualBytes);
        Assert.Empty(Directory.EnumerateDirectories(snapshots));
        Assert.True(File.Exists(sourcePath));
    }

    public void Dispose()
    {
        Log.Logger = previousLogger;
        logger.Dispose();
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public Action<LogEvent>? OnEmit { get; set; }

        public void Emit(LogEvent logEvent)
        {
            Events.Add(logEvent);
            OnEmit?.Invoke(logEvent);
        }
    }
}
