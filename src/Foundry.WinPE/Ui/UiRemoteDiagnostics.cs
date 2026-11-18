// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Runtime.InteropServices;
using Foundry.Core.Services.Runtime;
using Foundry.Telemetry;
using Foundry.Utilities.Runtime;

namespace Foundry.WinPE.Ui;

/// <summary>
/// Remote diagnostics (Logs and Error Tracking) of the UI process, under the telemetry application of the phase the session is
/// in: <c>foundry_connect</c> from the start (system preparation included) and <c>foundry_deploy</c> from the Deploy phase.
/// </summary>
/// <remarks>
/// Consent comes from the diagnostic preferences of <c>foundry.winpe.config.json</c>, read once at process start; without
/// them nothing is sent and the startup buffer is dropped. Each application gets its own diagnostics service, because a
/// service keeps the application of its first configuration in its log export: activating the next application swaps the
/// process-wide sink to a new service at once and drains the previous one in the background. Not thread-safe: activate on
/// the UI thread.
/// </remarks>
internal sealed class UiRemoteDiagnostics
{
    private readonly TelemetrySettings? _settings;
    private readonly List<Task> _retiring = [];
    private IRemoteDiagnosticsService? _current;
    private string? _application;

    internal UiRemoteDiagnostics(TelemetrySettings? settings)
    {
        _settings = settings;
        if (settings is null)
        {
            RemoteDiagnosticsSink.Clear();
        }
    }

    /// <summary>Reads the early consent of the WinPE runtime configuration.</summary>
    internal static UiRemoteDiagnostics FromConfiguration() =>
        new(RuntimeTelemetryConsent.ReadSettings(WinPeConfigurationPath.Resolve().Path));

    /// <summary>Sends the following diagnostics as <paramref name="application"/>; the same application again does nothing.</summary>
    /// <param name="application">A <see cref="TelemetryApps"/> value.</param>
    /// <param name="version">The version reported with <paramref name="application"/>.</param>
    internal void Activate(string application, string version)
    {
        if (_settings is null || _application == application)
        {
            return;
        }

        string runtime = WinPeRuntimeDetector.IsWinPeRuntime() ? TelemetryRuntimeModes.WinPe : TelemetryRuntimeModes.Desktop;
        TelemetryContext context = TelemetryContextFactory.Create(application, version, TelemetryBuildConfiguration.Current, runtime,
            string.IsNullOrWhiteSpace(_settings.RuntimePayloadSource) ? TelemetryRuntimePayloadSources.Unknown : _settings.RuntimePayloadSource,
            TelemetryBootMediaTargets.None, RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            CultureInfo.CurrentUICulture.Name, TelemetryRuntimeHosts.FoundryWinPe);
        var service = new PostHogRemoteDiagnosticsSink();
        RemoteDiagnosticsLifecycle.Initialize(service, _settings, context);
        if (_current is IRemoteDiagnosticsService previous)
        {
            _retiring.Add(Task.Run(() => RetireAsync(previous)));
        }

        _current = service;
        _application = application;
    }

    /// <summary>Drains and disposes every service within <see cref="HostedPhaseScope.ShutdownBudget"/>; never throws.</summary>
    internal async Task ShutdownAsync()
    {
        using var timeout = new CancellationTokenSource(HostedPhaseScope.ShutdownBudget);
        try
        {
            await Task.WhenAll(_retiring).WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch
        {
        }

        if (_current is null)
        {
            return;
        }

        try
        {
            await RemoteDiagnosticsLifecycle.ShutdownAsync(_current, timeout.Token).ConfigureAwait(false);
        }
        catch
        {
            // Pending diagnostics must not delay the process exit beyond the budget.
        }
    }

    // Not RemoteDiagnosticsLifecycle.ShutdownAsync: it would also detach the new service from the process-wide sink.
    private static async Task RetireAsync(IRemoteDiagnosticsService service)
    {
        using var timeout = new CancellationTokenSource(HostedPhaseScope.ShutdownBudget);
        try
        {
            await service.FlushAsync(timeout.Token).ConfigureAwait(false);
            await service.DisposeAsync().AsTask().WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch
        {
        }
    }
}
