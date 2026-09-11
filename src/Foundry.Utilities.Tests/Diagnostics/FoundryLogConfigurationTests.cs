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
    public void PrepareEvent_EnrichesAndMasksHandoffWithoutChangingSubsequentLogIdentity()
    {
        var sink = new CollectingSink();
        using var logger = (Logger)FoundryLogConfiguration.CreateDebugLogger("Foundry.Test", "SESSION01",
            LogEventLevel.Verbose, additionalSink: sink);
        using IDisposable context = Serilog.Context.LogContext.PushProperty("OperationId", "operation-1");
        using IDisposable secret = Serilog.Context.LogContext.PushProperty("Password", "private-secret");
        var source = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Fatal, new IOException("password=private-secret"),
            new Serilog.Parsing.MessageTemplateParser().Parse("Startup failed"),
            [new LogEventProperty("SourceContext", new ScalarValue("Foundry.Test.Startup"))]);

        LogEvent prepared = FoundryLogConfiguration.PrepareEvent(source, "Foundry.Test", "SESSION01");
        Assert.Empty(sink.Events);
        var properties = prepared.Properties.ToDictionary();
        logger.Write(prepared);

        LogEvent logged = Assert.Single(sink.Events);
        Assert.Equal(prepared.Timestamp, logged.Timestamp);
        Assert.Equal(properties.Keys.Order(), logged.Properties.Keys.Order());
        foreach ((string name, LogEventPropertyValue value) in properties)
            Assert.Equal(value.ToString(), logged.Properties[name].ToString());
        Assert.Equal("Startup", ((ScalarValue)properties["Component"]).Value);
        Assert.Equal("operation-1", ((ScalarValue)properties["OperationId"]).Value);
        Assert.DoesNotContain("private-secret", properties["Password"].ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private-secret", prepared.Exception!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CreateFileLogger_UsesInvariantMessageFormattingUnderFrenchCulture()
    {
        using var tempDirectory = new TemporaryDirectory();
        string path = Path.Combine(tempDirectory.Path, "Foundry.log");
        System.Globalization.CultureInfo original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
            ILogger logger = FoundryLogConfiguration.CreateFileLogger(path, "Foundry.Test", "SESSION01",
                LogEventLevel.Verbose, 2);
            try { logger.Information("Duration {Duration:F2}", 1.25); }
            finally { (logger as IDisposable)?.Dispose(); }
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = original; }

        Assert.Contains("Duration 1.25", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void CreateFileLogger_MasksSecretsBeforeBothOutputsAndPreservesTechnicalContext()
    {
        using var tempDirectory = new TemporaryDirectory();
        string path = Path.Combine(tempDirectory.Path, "Foundry.log");
        var sink = new CollectingSink();
        ILogger logger = FoundryLogConfiguration.CreateFileLogger(path, "Foundry.Test", "SESSION01",
            LogEventLevel.Verbose, 2, additionalSink: sink);
        try
        {
            logger.Information("Reading {Path} using {Password}; {@Context}", @"C:\Users\alice\boot.wim",
                "password-value", new { TenantId = "tenant-42", AccessToken = "token-value", Count = 3 });
            logger.Error(new InvalidOperationException("Request failed: Authorization: Bearer bearer-value\npassword=exception-value"),
                "GET https://storage.example/image.wim?version=42&sig=signature-value");
        }
        finally
        {
            (logger as IDisposable)?.Dispose();
        }

        string local = File.ReadAllText(path);
        string remote = string.Join("\n", sink.Events.Select(e => e.RenderMessage() + e.Exception));
        foreach (string output in new[] { local, remote })
        {
            Assert.DoesNotContain("password-value", output, StringComparison.Ordinal);
            Assert.DoesNotContain("token-value", output, StringComparison.Ordinal);
            Assert.DoesNotContain("bearer-value", output, StringComparison.Ordinal);
            Assert.DoesNotContain("exception-value", output, StringComparison.Ordinal);
            Assert.DoesNotContain("signature-value", output, StringComparison.Ordinal);
            Assert.Contains("alice", output, StringComparison.Ordinal);
            Assert.Contains("tenant-42", output, StringComparison.Ordinal);
            Assert.Contains("version=42", output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CreateFileLogger_PreservesAllLevelsAndStableIdentityAcrossFanout()
    {
        using var tempDirectory = new TemporaryDirectory();
        string path = Path.Combine(tempDirectory.Path, "Foundry.log");
        var sink = new CollectingSink();
        ILogger logger = FoundryLogConfiguration.CreateFileLogger(path, "Foundry.Test", "SESSION01",
            LogEventLevel.Verbose, 2, additionalSink: sink);
        var timestamp = new DateTimeOffset(2026, 9, 10, 12, 34, 56, TimeSpan.Zero);
        try
        {
            foreach (LogEventLevel level in Enum.GetValues<LogEventLevel>())
            {
                logger.Write(new LogEvent(timestamp, level, null,
                    new Serilog.Parsing.MessageTemplateParser().Parse("Repeated diagnostic"), []));
            }
        }
        finally
        {
            (logger as IDisposable)?.Dispose();
        }

        Assert.Equal(6, sink.Events.Count);
        Assert.All(sink.Events, e => Assert.Equal(timestamp, e.Timestamp));
        string local = File.ReadAllText(path);
        Assert.Equal(6, sink.Events.Select(e => e.Properties["diagnostics.record_id"].ToString()).Distinct().Count());
        foreach (LogEvent entry in sink.Events)
        {
            Assert.Contains(((ScalarValue)entry.Properties["diagnostics.record_id"]).Value!.ToString()!, local, StringComparison.Ordinal);
        }
        long[] sequences = sink.Events.Select(e => (long)((ScalarValue)e.Properties["diagnostics.sequence"]).Value!).ToArray();
        Assert.True(sequences.Zip(sequences.Skip(1), (left, right) => left < right).All(value => value));
    }

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
