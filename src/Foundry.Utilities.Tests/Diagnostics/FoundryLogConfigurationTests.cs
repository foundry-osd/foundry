// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Diagnostics;
using Foundry.Utilities.Tests.IO;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Foundry.Utilities.Tests.Diagnostics;

public sealed class FoundryLogConfigurationTests
{
    [Fact]
    public void CreateFileLogger_ForwardsEventsToAdditionalSink()
    {
        using var tempDirectory = new TemporaryDirectory();
        var sink = new CollectingSink();

        ILogger logger = FoundryLogConfiguration.CreateFileLogger(
            Path.Combine(tempDirectory.Path, "Foundry.log"),
            "Foundry.Test",
            "SESSION01",
            LogEventLevel.Information,
            retainedFileCountLimit: 2,
            additionalSink: sink);

        try
        {
            logger.Information("Operation completed. OperationId={OperationId}", "operation-1");
        }
        finally
        {
            (logger as IDisposable)?.Dispose();
        }

        LogEvent logEvent = Assert.Single(sink.Events);
        Assert.Equal("Foundry.Test", logEvent.Properties["Application"].ToString().Trim('"'));
        Assert.Equal("SESSION01", logEvent.Properties["SessionId"].ToString().Trim('"'));
    }

    [Fact]
    public void CreateFileLogger_WritesCorrelatedStructuredDebugEvent()
    {
        using var tempDirectory = new TemporaryDirectory();
        string logFilePath = Path.Combine(tempDirectory.Path, "Foundry.log");

        ILogger logger = FoundryLogConfiguration.CreateFileLogger(
            logFilePath,
            "Foundry.Test",
            "SESSION01",
            LogEventLevel.Debug,
            retainedFileCountLimit: 2);
        try
        {
            logger.ForContext<SupportBundleExporter>()
                .Debug("Diagnostic operation completed. ItemCount={ItemCount}", 42);
        }
        finally
        {
            (logger as IDisposable)?.Dispose();
        }

        string output = File.ReadAllText(logFilePath);
        Assert.Matches(
            @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z \[DBG\] \[Foundry\.Test\] \[Session:SESSION01\] \[SupportBundleExporter\] ",
            output);
        Assert.Contains("Diagnostic operation completed. ItemCount=42", output, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateDebugLogger_ForwardsEventsToAdditionalSink()
    {
        var sink = new CollectingSink();

        ILogger logger = FoundryLogConfiguration.CreateDebugLogger(
            "Foundry.Test",
            "SESSION01",
            LogEventLevel.Debug,
            additionalSink: sink);

        try
        {
            logger.Error("Bootstrap failed. OperationId={OperationId}", "operation-1");
        }
        finally
        {
            (logger as IDisposable)?.Dispose();
        }

        LogEvent logEvent = Assert.Single(sink.Events);
        Assert.Equal("Foundry.Test", logEvent.Properties["Application"].ToString().Trim('"'));
        Assert.Equal("SESSION01", logEvent.Properties["SessionId"].ToString().Trim('"'));
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent)
        {
            Events.Add(logEvent);
        }
    }
}
