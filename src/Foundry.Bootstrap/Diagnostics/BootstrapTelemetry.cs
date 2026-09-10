// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
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

    /// <summary>Enables capture only after valid media preferences; no transport starts here.</summary>
    internal void Configure(string winPeRoot, string? cacheRoot)
    {
        try
        {
            root = winPeRoot;
            settings = BootstrapTelemetryConsent.ReadBootstrap(ReadFile(Path.Combine(root, "Config", "foundry.bootstrap.config.json")));
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
        try { RestrictChildConsent(); pipeline?.StartDelivery(clockUsable); }
        catch { }
    }

    /// <summary>Maps only terminal failures to product events; diagnostics use their own consent.</summary>
    internal void Complete(BootstrapResult result, TimeSpan elapsed)
    {
        if (result.Outcome != BootstrapOutcome.Failed) { return; }
        try
        {
            RestrictChildConsent();
            pipeline?.CaptureTerminalFailure(new Dictionary<string, object?>
            {
                ["failure_category"] = result.ChildExitCode.HasValue ? "child_exit" : "stage_failed",
                ["last_stage"] = result.Stage.ToString().ToLowerInvariant(),
                ["elapsed_seconds"] = elapsed.TotalSeconds,
                ["child_application"] = result.Stage == BootstrapStage.Connect ? "foundry_connect" : result.Stage == BootstrapStage.Deploy ? "foundry_deploy" : "none",
                ["child_exit_code"] = result.ChildExitCode,
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
        var consent = BootstrapTelemetryConsent.RestrictChild(connectJson,
            required, settings.IsEnabled, settings.IsRemoteDiagnosticsEnabled);
        consent = BootstrapTelemetryConsent.RestrictChild(ReadFile(Path.Combine(root, "Config", "foundry.deploy.config.json")), false, consent.Usage, consent.Diagnostics);
        pipeline.RestrictConsent(consent.Usage, consent.Diagnostics);
    }

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
