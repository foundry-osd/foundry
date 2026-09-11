// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Foundry.Core.Models.Runtime;
using Foundry.Core.Services.Runtime;
using Foundry.Telemetry;
using Foundry.Utilities.Diagnostics;
using Serilog;
using Serilog.Events;
using Serilog.Parsing;

namespace Foundry.Deploy.Services.Runtime;

/// <summary>Owns child startup acknowledgements and transfers only pre-readiness failure evidence to Bootstrap.</summary>
public sealed class RuntimeStartupDiagnostics
{
    private readonly Action<string, string?, string?> _report;
    private readonly Func<Exception, LogEvent, string, Guid?> _capture;
    private readonly Func<ILogger> _logger;
    private readonly TelemetrySettings? _earlySettings;
    private readonly RemoteDiagnosticsContext? _earlyContext;
    private string? _emergencyPath;
    private int _failed;
    private int _ready;

    internal RuntimeStartupDiagnostics(Action<string, string?, string?> report,
        Func<Exception, LogEvent, string, Guid?> capture, ILogger logger)
    {
        _report = report;
        _capture = capture;
        _logger = () => logger;
    }

    internal RuntimeStartupDiagnostics(RuntimeStartupReporter? reporter, TelemetrySettings? settings)
    {
        ProtocolEnabled = reporter is not null;
        _report = (stage, category, id) => reporter?.Report(stage, category, id);
        _logger = () => Log.ForContext<RuntimeStartupDiagnostics>();
        _emergencyPath = reporter is null ? null : Path.Combine(Path.GetDirectoryName(reporter.StatusPath)!, "startup-terminated.txt");
        var context = new RemoteDiagnosticsContext(TelemetryApps.FoundryDeploy, FoundryDeployApplicationInfo.Version,
            TelemetryBuildConfiguration.Current, TelemetryRuntimeModes.WinPe,
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), CultureInfo.CurrentUICulture.Name,
            reporter?.SessionId ?? DiagnosticSessionContext.CurrentSessionId,
            TelemetryApps.FoundryDeploy + "@" + FoundryDeployApplicationInfo.Version);
        _earlySettings = settings;
        _earlyContext = context;
        _capture = (exception, entry, category) =>
        {
            if (reporter is null || settings is null) return null;
            var options = new RemoteDiagnosticsOptions(settings.IsRemoteDiagnosticsEnabled,
                settings.HostUrl, settings.ProjectToken, settings.InstallId);
            return ChildStartupFailureExchange.TryCapture(Path.GetDirectoryName(reporter.StatusPath)!,
                Guid.Parse(reporter.LaunchId), options, context, entry);
        };
    }

    /// <summary>Indicates that Bootstrap supplied a valid negotiated launch identity.</summary>
    public bool ProtocolEnabled { get; }

    /// <summary>Loads only early diagnostic preferences; standalone startup never gains consent from Bootstrap files.</summary>
    internal static RuntimeStartupDiagnostics Create(string childConfigurationPath, bool configurationRequired = true)
    {
        RuntimeStartupReporter? reporter = RuntimeStartupReporter.FromEnvironment("Foundry.Deploy");
        reporter?.Report(StartupStage.ManagedStarted);
        TelemetrySettings? settings = reporter is null ? null : RuntimeTelemetryConsent.ReadSettings(
            @"X:\Foundry\Config\foundry.bootstrap.config.json", childConfigurationPath, configurationRequired);
        return new RuntimeStartupDiagnostics(reporter, settings);
    }

    /// <summary>Starts the process service with verified managed-startup consent before full configuration loading.</summary>
    internal void InitializeRemoteDiagnostics(IRemoteDiagnosticsService service)
    {
        if (_earlySettings is null || _earlyContext is null) return;
        var context = new TelemetryContext(_earlyContext.App, _earlyContext.AppVersion,
            _earlyContext.BuildConfiguration, _earlyContext.Runtime, _earlySettings.RuntimePayloadSource,
            TelemetryBootMediaTargets.None, _earlyContext.RuntimeArchitecture, _earlyContext.Locale, _earlyContext.SessionId);
        RemoteDiagnosticsLifecycle.Initialize(service, _earlySettings, context);
    }

    /// <summary>Publishes successful configuration loading before the UI is constructed.</summary>
    public void ReportConfigurationLoaded() => Report(StartupStage.ConfigurationLoaded);

    /// <summary>Publishes the first usable UI once; a recorded failure always takes precedence.</summary>
    public void ReportUiReady()
    {
        if (Volatile.Read(ref _failed) == 0 && Interlocked.Exchange(ref _ready, 1) == 0)
            Report(StartupStage.UiReady);
    }

    /// <summary>Awaits owned initialization and a render opportunity without blocking the WPF dispatcher.</summary>
    public async Task ObserveInitializationAsync(Func<Task> initialize, Func<Task> rendered, Func<bool> isOpen)
    {
        await initialize();
        if (!isOpen()) return;
        await rendered();
        if (isOpen()) ReportUiReady();
    }

    /// <summary>Retains the original exception; reports a persisted record ID before orderly application shutdown.</summary>
    public void ReportFailure(Exception exception, string category)
    {
        if (Interlocked.Exchange(ref _failed, 1) != 0) return;
        Guid? recordId = null;
        try
        {
            var entry = FoundryLogConfiguration.PrepareEvent(new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Fatal, exception,
                new MessageTemplateParser().Parse("Application failed at {FailureReason}."),
                [new LogEventProperty("FailureReason", new ScalarValue(category)),
                 new LogEventProperty("SourceContext", new ScalarValue(typeof(RuntimeStartupDiagnostics).FullName))]),
                "Foundry.Deploy", DiagnosticSessionContext.CurrentSessionId);
            try
            {
                if (Volatile.Read(ref _ready) == 0) recordId = _capture(exception, entry, category);
            }
            catch { }
            try
            {
                _logger().ForContext("RemoteDiagnosticsInternal", recordId.HasValue).Write(entry);
            }
            catch { }
        }
        catch { }
        finally
        {
            Report(StartupStage.StartupFailed, category, recordId?.ToString("N"));
        }
    }

    private void Report(string stage, string? category = null, string? recordId = null)
    {
        try { _report(stage, category, recordId); }
        catch { }
    }

    /// <summary>Prepares a local evidence destination while normal startup is still safe to perform path work.</summary>
    internal void SetLocalLogPath(string path)
    {
        if (_emergencyPath is null && Path.IsPathFullyQualified(path))
            _emergencyPath = Path.Combine(Path.GetDirectoryName(path)!, "FoundryDeploy-terminated.txt");
    }

    /// <summary>Attempts minimal local evidence during process termination; never touches log sinks, services or transport.</summary>
    internal void RecordTerminatingException(Exception? exception)
    {
        try
        {
            if (_emergencyPath is not null)
                File.WriteAllText(_emergencyPath, exception?.GetType().FullName ?? "Unhandled terminating exception");
        }
        catch { }
    }
}
