// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

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
        "{UtcTimestamp:yyyy-MM-ddTHH:mm:ss.fff'Z'} [{Level:u3}] [{Application}] [Session:{SessionId}] [{Component}] {Message:lj}{NewLine}{Exception}";

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
        ConfigureAdditionalSink(configuration, additionalSink);
        return configuration
            .WriteTo.File(
                logFilePath,
                outputTemplate: OutputTemplate,
                fileSizeLimitBytes: DefaultFileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: retainedFileCountLimit,
                shared: true,
                flushToDiskInterval: TimeSpan.FromSeconds(1))
            .WriteTo.Debug(outputTemplate: OutputTemplate)
            .CreateLogger();
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
        ConfigureAdditionalSink(configuration, additionalSink);
        return configuration
            .WriteTo.Debug(outputTemplate: OutputTemplate)
            .CreateLogger();
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
}
