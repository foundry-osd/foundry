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

namespace Foundry.Bootstrap.Diagnostics;

/// <summary>Adapts boot outcomes and consent to the shared, best-effort telemetry pipeline.</summary>
internal sealed class BootstrapTelemetry : ILogEventSink
{
    private BootstrapTelemetryPipeline? pipeline;
    private string root = string.Empty;
    private TelemetrySettings? settings;
    private string? recoveryRoot;
    private Task recovery = Task.CompletedTask;
    private int terminalOutcomeRecorded;

    /// <summary>Enables capture only after valid media preferences; no transport starts here.</summary>
    internal void Configure(string winPeRoot, string? cacheRoot)
    {
        try
        {
            root = winPeRoot;
            settings = RuntimeTelemetryConsent.ReadBootstrap(ReadFile(Path.Combine(root, "Config", "foundry.bootstrap.config.json")));
            recoveryRoot = Path.Combine(cacheRoot ?? winPeRoot, "Logs");
            if (settings is null) { return; }
            string version = typeof(BootstrapTelemetry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
            var context = TelemetryContextFactory.Create(TelemetryApps.FoundryBootstrap, version, TelemetryBuildConfiguration.Current,
                "winpe", settings.RuntimePayloadSource, cacheRoot is null ? "iso" : "usb",
                RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(), CultureInfo.CurrentUICulture.Name);
            pipeline = new BootstrapTelemetryPipeline(
                new(settings.IsEnabled, settings.HostUrl, settings.ProjectToken, settings.InstallId), context,
                new(settings.IsRemoteDiagnosticsEnabled, settings.HostUrl, settings.ProjectToken, settings.InstallId),
                TelemetryContextFactory.CreateRemoteDiagnosticsContext(context),
                cacheRoot is null ? null : Path.Combine(cacheRoot, "Diagnostics", "Bootstrap", "pending.jsonl"));
            RestrictChildConsent();
        }
        catch { }
    }

    public void Emit(LogEvent logEvent)
    {
        try { pipeline?.Emit(logEvent); }
        catch { }
    }

    /// <summary>Rechecks child opt-outs before the coordinator permits background delivery.</summary>
    internal void StartDelivery(bool clockUsable)
    {
        try { RestrictChildConsent(); }
        catch { return; }
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

    /// <summary>Maps only terminal failures to product events; diagnostics use their own consent.</summary>
    internal void Complete(BootstrapResult result, TimeSpan elapsed)
    {
        if (Interlocked.Exchange(ref terminalOutcomeRecorded, 1) != 0 || result.Outcome != BootstrapOutcome.Failed) { return; }
        try
        {
            RestrictChildConsent();
            pipeline?.CaptureTerminalFailure(new Dictionary<string, object?>
            {
                ["failure_category"] = result.FailureCategory ?? (result.ChildExitCode.HasValue ? "child_exit" : "stage_failed"),
                ["last_stage"] = result.Stage.ToString().ToLowerInvariant(),
                ["elapsed_seconds"] = elapsed.TotalSeconds,
                ["child_application"] = result.Stage == BootstrapStage.Connect ? "foundry_connect" : result.Stage == BootstrapStage.Deploy ? "foundry_deploy" : "none",
                ["child_exit_code"] = result.ChildExitCode,
                ["child_startup_stage"] = result.ChildStartupStage,
                ["payload_source"] = settings?.RuntimePayloadSource,
                ["architecture"] = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()
            });
        }
        catch { }
    }

    internal async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            RestrictChildConsent();
            try { await recovery.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch { }
            if (pipeline is not null) { await pipeline.ShutdownAsync(cancellationToken).ConfigureAwait(false); }
        }
        catch { }
    }

    private void RestrictChildConsent()
    {
        if (settings is null || pipeline is null) { return; }
        string? connectOverride = Environment.GetEnvironmentVariable("FOUNDRY_CONNECT_CONFIG");
        bool required = !string.IsNullOrWhiteSpace(connectOverride);
        // Connect resolves relative overrides under its executable directory, which differs from Bootstrap.
        string? connectJson = required && !Path.IsPathFullyQualified(connectOverride!) ? "invalid" :
            ReadFile(required ? connectOverride! : Path.Combine(root, "Config", "foundry.connect.config.json"));
        var consent = RuntimeTelemetryConsent.RestrictChild(connectJson,
            required, settings.IsEnabled, settings.IsRemoteDiagnosticsEnabled);
        consent = RuntimeTelemetryConsent.RestrictChild(ReadFile(Path.Combine(root, "Config", "foundry.deploy.config.json")), false, consent.Usage, consent.Diagnostics);
        pipeline.RestrictConsent(consent.Usage, consent.Diagnostics);
    }

    /// <summary>Called only after the launcher has observed child exit and released upload ownership.</summary>
    internal void RecoverChildFailure(string directory, Guid launchId, string application)
    {
        try
        {
            RestrictChildConsent();
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

    private static string? ReadFile(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            if (file.Length > 1024 * 1024) { return "invalid"; }
            using var reader = new StreamReader(file);
            return reader.ReadToEnd();
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch { return "invalid"; }
    }
}
