// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Foundry.Core.Services.Runtime;
using Foundry.Telemetry;
using Serilog.Core;
using Serilog.Events;

namespace Foundry.WinPE.Diagnostics;

/// <summary>Adapts boot outcomes and consent to the shared, best-effort telemetry pipeline.</summary>
internal sealed class BootstrapTelemetry : ILogEventSink
{
    private BootstrapTelemetryPipeline? pipeline;
    private TelemetrySettings? settings;
    private string? recoveryRoot;
    private Task recovery = Task.CompletedTask;
    private int terminalOutcomeRecorded;
    private int deliveryStarted;
    private string bootMediaTarget = TelemetryBootMediaTargets.Unknown;

    /// <summary>
    /// Enables capture only after valid preferences in <c>foundry.winpe.config.json</c>, the one consent source shared with
    /// Connect and Deploy; no transport starts here.
    /// </summary>
    internal void Configure(string winPeRoot, string? cacheRoot)
    {
        try
        {
            bootMediaTarget = cacheRoot is null ? TelemetryBootMediaTargets.Iso : TelemetryBootMediaTargets.Usb;
            settings = ReadConsent(winPeRoot, Environment.GetEnvironmentVariable(WinPeConfigurationPath.OverrideEnvironmentVariable));
            recoveryRoot = Path.Combine(cacheRoot ?? winPeRoot, "Logs");
            if (settings is null) { return; }
            TelemetryContext context = CreateContext(settings.RuntimePayloadSource, bootMediaTarget);
            pipeline = new BootstrapTelemetryPipeline(
                new(settings.IsEnabled, settings.HostUrl, settings.ProjectToken, settings.InstallId), context,
                new(settings.IsRemoteDiagnosticsEnabled, settings.HostUrl, settings.ProjectToken, settings.InstallId),
                TelemetryContextFactory.CreateRemoteDiagnosticsContext(context),
                cacheRoot is null ? null : Path.Combine(cacheRoot, "Diagnostics", "Bootstrap", "pending.jsonl"));
        }
        catch { }
    }

    /// <summary>
    /// Reads the consent of <c>Config\foundry.winpe.config.json</c> under <paramref name="winPeRoot"/>, or of
    /// <paramref name="configurationOverride"/> when <see cref="WinPeConfigurationPath.OverrideEnvironmentVariable"/> is set.
    /// </summary>
    internal static TelemetrySettings? ReadConsent(string winPeRoot, string? configurationOverride) =>
        RuntimeTelemetryConsent.ReadSettings(WinPeConfigurationPath.Resolve(
            Path.Combine(winPeRoot, "Config", WinPeWorkspaceLayout.ConfigurationFileName),
            configurationOverride).Path);

    /// <summary>
    /// Creates the guardian's event context: application <see cref="TelemetryApps.FoundryBootstrap"/> hosted by
    /// <see cref="TelemetryRuntimeHosts.FoundryWinPe"/>, so its events carry <c>runtime_host</c>.
    /// </summary>
    internal static TelemetryContext CreateContext(string payloadSource, string bootMediaTarget)
    {
        string version = typeof(BootstrapTelemetry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        return TelemetryContextFactory.Create(TelemetryApps.FoundryBootstrap, version, TelemetryBuildConfiguration.Current,
            "winpe", payloadSource, bootMediaTarget,
            RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(), CultureInfo.CurrentUICulture.Name,
            TelemetryRuntimeHosts.FoundryWinPe);
    }

    public void Emit(LogEvent logEvent)
    {
        try { pipeline?.Emit(logEvent); }
        catch { }
    }

    /// <summary>
    /// Starts background delivery when the guardian has something to send; only the first call starts it, with the clock
    /// usability the caller evaluated at that moment.
    /// </summary>
    internal void StartDelivery(bool clockUsable)
    {
        if (Interlocked.Exchange(ref deliveryStarted, 1) != 0) { return; }
        try
        {
            pipeline?.StartDelivery(clockUsable);
            recovery = Task.Run(() =>
            {
                try { RecoverPendingChildFailures(); }
                catch { }
            });
        }
        catch { }
    }

    /// <summary>
    /// Maps only terminal failures to product events, at most once per boot; diagnostics use their own consent. The payload
    /// version is the version of the application that failed, not the guardian version carried by the event context.
    /// </summary>
    internal void Complete(BootstrapResult result, TimeSpan elapsed)
    {
        if (Interlocked.Exchange(ref terminalOutcomeRecorded, 1) != 0 || result.Outcome != BootstrapOutcome.Failed) { return; }
        try
        {
            pipeline?.CaptureTerminalFailure(FailureProperties(result, elapsed, settings?.RuntimePayloadSource, bootMediaTarget));
        }
        catch { }
    }

    /// <summary>
    /// Builds the <c>bootstrap:failed</c> properties. The UI process phases map to the application that runs them: Foundry
    /// Connect for <see cref="BootstrapStage.Connect"/> and for the system preparation that follows it
    /// (<see cref="BootstrapStage.System"/>), Foundry Deploy for <see cref="BootstrapStage.Deploy"/>, none for the guardian's
    /// own environment stage.
    /// </summary>
    internal static IReadOnlyDictionary<string, object?> FailureProperties(BootstrapResult result, TimeSpan elapsed,
        string? payloadSource, string bootMediaTarget) => new Dictionary<string, object?>
        {
            ["failure_category"] = result.FailureCategory ?? (result.ChildExitCode.HasValue ? "child_exit" : "stage_failed"),
            ["last_stage"] = result.Stage.ToString().ToLowerInvariant(),
            ["elapsed_seconds"] = elapsed.TotalSeconds,
            ["child_application"] = result.Stage switch
            {
                BootstrapStage.Connect or BootstrapStage.System => TelemetryApps.FoundryConnect,
                BootstrapStage.Deploy => TelemetryApps.FoundryDeploy,
                _ => "none"
            },
            ["child_exit_code"] = result.ChildExitCode,
            ["child_startup_stage"] = result.ChildStartupStage,
            ["payload_source"] = payloadSource,
            ["payload_version"] = result.PayloadVersion,
            ["boot_media_target"] = bootMediaTarget,
            ["architecture"] = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()
        };

    internal async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            try { await recovery.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch { }
            if (pipeline is not null) { await pipeline.ShutdownAsync(cancellationToken).ConfigureAwait(false); }
        }
        catch { }
    }

    /// <summary>Called only after the launcher has observed child exit and released upload ownership.</summary>
    internal void RecoverChildFailure(string directory, Guid launchId, string application)
    {
        try
        {
            var status = RuntimeStartupStatusReader.ReadValidated(Path.Combine(directory, "status.json"));
            pipeline?.ImportChildStartupFailure(directory, launchId, TelemetryApplication(application), childExited: true,
                expectedRecordId: Guid.TryParse(status?.FailureRecordId, out Guid recordId) ? recordId : null);
        }
        catch { }
    }

    private void RecoverPendingChildFailures()
    {
        if (pipeline is null || recoveryRoot is null || !Directory.Exists(recoveryRoot)) { return; }
        var options = new EnumerationOptions
        {
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            MaxRecursionDepth = 3
        };
        foreach (string file in Directory.EnumerateFiles(recoveryRoot, ChildStartupFailureExchange.FileName, options).Take(100))
        {
            string directory = Path.GetDirectoryName(file)!;
            DirectoryInfo? startup = Directory.GetParent(directory);
            if (startup?.Name != "Startup" || startup.Parent?.Parent?.FullName != Path.GetFullPath(recoveryRoot)) { continue; }
            var status = RuntimeStartupStatusReader.ReadValidated(Path.Combine(directory, "status.json"));
            if (status is null || status.SessionId != startup.Parent.Name ||
                status.LaunchId != Path.GetFileName(directory) || !Guid.TryParse(status.LaunchId, out Guid launchId)) { continue; }
            // A reused PID is deliberately conservative: leave the record until ownership is certain.
            try { using Process child = Process.GetProcessById(status.ProcessId); continue; }
            catch (ArgumentException) { }
            catch { continue; }
            pipeline.ImportChildStartupFailure(directory, launchId, TelemetryApplication(status.Application), childExited: true,
                expectedRecordId: Guid.TryParse(status.FailureRecordId, out Guid recordId) ? recordId : null);
        }
    }

    private static string TelemetryApplication(string application) => application switch
    {
        "Foundry.Connect" => TelemetryApps.FoundryConnect,
        "Foundry.Deploy" => TelemetryApps.FoundryDeploy,
        _ => string.Empty
    };
}
