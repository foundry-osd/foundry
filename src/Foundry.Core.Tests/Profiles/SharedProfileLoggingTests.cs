// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

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

    public void Dispose()
    {
        Log.Logger = previousLogger;
        logger.Dispose();
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
