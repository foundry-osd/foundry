// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Runtime;
using Foundry.Utilities.Diagnostics;
using Serilog;
using Serilog.Events;
using Serilog.Parsing;

namespace Foundry.Core.Services.Runtime;

/// <summary>
/// Startup diagnostics of a Connect or Deploy phase hosted by the Foundry.WinPE UI process, which owns the startup protocol:
/// failures are logged, and the stages the phase reaches are forwarded to the host's hooks (see
/// <see cref="CreateForHostedPhase"/>).
/// </summary>
public sealed class RuntimeStartupDiagnostics
{
    private readonly RuntimeApplicationIdentity _identity;
    private readonly Action<string, string?> _report;
    private readonly Func<ILogger> _logger;
    private int _failed;
    private int _ready;

    internal RuntimeStartupDiagnostics(RuntimeApplicationIdentity identity, Action<string, string?> report, ILogger logger)
        : this(identity, report, () => logger)
    {
    }

    private RuntimeStartupDiagnostics(RuntimeApplicationIdentity identity, Action<string, string?> report, Func<ILogger> logger)
    {
        _identity = identity;
        _report = report;
        _logger = logger;
    }

    /// <summary>
    /// Creates the diagnostics of a phase hosted by the Foundry.WinPE UI process: nothing is reported to the guardian, failures
    /// are only logged, and the stages the host reports for the phase reach it through hooks:
    /// <see cref="ReportConfigurationLoaded"/> calls <paramref name="onConfigurationLoaded"/> and the first usable UI calls
    /// <paramref name="onUiReady"/> once.
    /// </summary>
    /// <param name="identity">The phase that starts, used for its log entries.</param>
    /// <param name="onUiReady">Called once, on the reporting thread, when the phase's first content or prompt rendered.</param>
    /// <param name="onConfigurationLoaded">
    /// Called on the reporting thread when the phase loaded its configuration and built its services, or
    /// <see langword="null"/> when the host does not need it.
    /// </param>
    public static RuntimeStartupDiagnostics CreateForHostedPhase(
        RuntimeApplicationIdentity identity,
        Action onUiReady,
        Action? onConfigurationLoaded = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(onUiReady);
        return new RuntimeStartupDiagnostics(identity,
            (stage, _) =>
            {
                if (stage == StartupStage.ConfigurationLoaded) onConfigurationLoaded?.Invoke();
                else if (stage == StartupStage.UiReady) onUiReady();
            },
            static () => Log.ForContext<RuntimeStartupDiagnostics>());
    }

    /// <summary>Publishes successful configuration loading before the UI is constructed.</summary>
    public void ReportConfigurationLoaded() => Report(StartupStage.ConfigurationLoaded);

    /// <summary>Indicates that the first usable UI was reported; failures after this point are runtime failures.</summary>
    public bool IsUiReady => Volatile.Read(ref _ready) != 0;

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

    /// <summary>Logs the original exception once and reports the failure; readiness is never reported after it.</summary>
    public void ReportFailure(Exception exception, string category)
    {
        if (Interlocked.Exchange(ref _failed, 1) != 0) return;
        try
        {
            var entry = FoundryLogConfiguration.PrepareEvent(new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Fatal, exception,
                new MessageTemplateParser().Parse("Application failed at {FailureReason}."),
                [new LogEventProperty("FailureReason", new ScalarValue(category)),
                 new LogEventProperty("SourceContext", new ScalarValue(typeof(RuntimeStartupDiagnostics).FullName))]),
                _identity.ApplicationId, DiagnosticSessionContext.CurrentSessionId);
            try
            {
                _logger().Write(entry);
            }
            catch { }
        }
        catch { }
        finally
        {
            Report(StartupStage.StartupFailed, category);
        }
    }

    private void Report(string stage, string? category = null)
    {
        try { _report(stage, category); }
        catch { }
    }
}
