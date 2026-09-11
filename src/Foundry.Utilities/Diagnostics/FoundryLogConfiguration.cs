// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Foundry.Utilities.Diagnostics;

/// <summary>
/// Creates the bounded, correlated structured-text logger shared by Foundry applications.
/// </summary>
public static class FoundryLogConfiguration
{
    public const long DefaultFileSizeLimitBytes = 10 * 1024 * 1024;

    public const string OutputTemplate =
        "{UtcTimestamp:yyyy-MM-ddTHH:mm:ss.fff'Z'} [{Level:u3}] [{Application}] [Session:{SessionId}] [{Component}] {Message:lj} {Properties:j}{NewLine}{Exception}";

    /// <summary>
    /// Prepares an event for durable handoff with the same context and masking as local logging,
    /// without writing to any output. Subsequent logging preserves its identity and timestamp.
    /// </summary>
    public static LogEvent PrepareEvent(LogEvent logEvent, string applicationName, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var configuration = new LoggerConfiguration().MinimumLevel.Verbose();
        ConfigureEnrichment(configuration, applicationName, sessionId);
        var sink = new PreparedEventSink();
        using Logger logger = configuration.WriteTo.Sink(sink).CreateLogger();
        logger.Write(logEvent);
        return LogEventNormalizer.Normalize(sink.Event ?? logEvent);
    }

    /// <summary>
    /// Creates a logger with a stable active filename, size-based rolling, and bounded retention.
    /// </summary>
    public static ILogger CreateFileLogger(
        string logFilePath,
        string applicationName,
        string sessionId,
        LogEventLevel minimumLevel,
        int retainedFileCountLimit,
        LoggingLevelSwitch? levelSwitch = null,
        ILogEventSink? additionalSink = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retainedFileCountLimit);

        var configuration = new LoggerConfiguration();
        ConfigureMinimumLevel(configuration, minimumLevel, levelSwitch);

        ConfigureEnrichment(configuration, applicationName, sessionId);
        var destinations = new LoggerConfiguration().MinimumLevel.Verbose();
        ConfigureAdditionalSink(destinations, additionalSink);
        Logger output = destinations
            .WriteTo.File(
                logFilePath,
                outputTemplate: OutputTemplate,
                formatProvider: CultureInfo.InvariantCulture,
                fileSizeLimitBytes: DefaultFileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: retainedFileCountLimit,
                shared: true,
                flushToDiskInterval: TimeSpan.FromSeconds(1))
            .WriteTo.Debug(outputTemplate: OutputTemplate, formatProvider: CultureInfo.InvariantCulture)
            .CreateLogger();
        return configuration.WriteTo.Sink(new NormalizingLogSink(output)).CreateLogger();
    }

    /// <summary>
    /// Creates a debugger-only fallback when no diagnostic file can be opened.
    /// </summary>
    public static ILogger CreateDebugLogger(
        string applicationName,
        string sessionId,
        LogEventLevel minimumLevel,
        LoggingLevelSwitch? levelSwitch = null,
        ILogEventSink? additionalSink = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var configuration = new LoggerConfiguration();
        ConfigureMinimumLevel(configuration, minimumLevel, levelSwitch);
        ConfigureEnrichment(configuration, applicationName, sessionId);
        var destinations = new LoggerConfiguration().MinimumLevel.Verbose();
        ConfigureAdditionalSink(destinations, additionalSink);
        Logger output = destinations
            .WriteTo.Debug(outputTemplate: OutputTemplate, formatProvider: CultureInfo.InvariantCulture)
            .CreateLogger();
        return configuration.WriteTo.Sink(new NormalizingLogSink(output)).CreateLogger();
    }

    private static void ConfigureMinimumLevel(
        LoggerConfiguration configuration,
        LogEventLevel minimumLevel,
        LoggingLevelSwitch? levelSwitch)
    {
        if (levelSwitch is null)
        {
            configuration.MinimumLevel.Is(minimumLevel);
        }
        else
        {
            configuration.MinimumLevel.ControlledBy(levelSwitch);
        }
    }

    private static void ConfigureEnrichment(
        LoggerConfiguration configuration,
        string applicationName,
        string sessionId)
    {
        configuration
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", LogValueSanitizer.NormalizePropertyValue(applicationName))
            .Enrich.WithProperty("SessionId", DiagnosticSessionContext.ResolveSessionId(sessionId))
            .Enrich.With<UtcTimestampEnricher>()
            .Enrich.With<SourceComponentEnricher>();
    }

    private static void ConfigureAdditionalSink(LoggerConfiguration configuration, ILogEventSink? additionalSink)
    {
        if (additionalSink is not null)
        {
            configuration.WriteTo.Sink(additionalSink);
        }
    }

    private sealed class PreparedEventSink : ILogEventSink
    {
        public LogEvent? Event { get; private set; }
        public void Emit(LogEvent logEvent) => Event = logEvent;
    }
}
