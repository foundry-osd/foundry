// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Diagnostics;

namespace Foundry.Telemetry;

/// <summary>
/// Creates shared telemetry and remote diagnostics contexts with the process correlation identifier.
/// </summary>
public static class TelemetryContextFactory
{
    /// <summary>
    /// Creates a telemetry context correlated with the current diagnostic session.
    /// </summary>
    public static TelemetryContext Create(
        string app,
        string appVersion,
        string buildConfiguration,
        string runtime,
        string runtimePayloadSource,
        string bootMediaTarget,
        string runtimeArchitecture,
        string locale)
    {
        return new TelemetryContext(
            app,
            appVersion,
            buildConfiguration,
            runtime,
            runtimePayloadSource,
            bootMediaTarget,
            runtimeArchitecture,
            locale,
            DiagnosticSessionContext.CurrentSessionId);
    }

    /// <summary>
    /// Creates remote diagnostics context from the shared telemetry context.
    /// </summary>
    public static RemoteDiagnosticsContext CreateRemoteDiagnosticsContext(TelemetryContext telemetryContext)
    {
        ArgumentNullException.ThrowIfNull(telemetryContext);

        return new RemoteDiagnosticsContext(
            telemetryContext.App,
            telemetryContext.AppVersion,
            telemetryContext.BuildConfiguration,
            telemetryContext.Runtime,
            telemetryContext.RuntimeArchitecture,
            telemetryContext.Locale,
            telemetryContext.SessionId,
            $"{telemetryContext.App}@{telemetryContext.AppVersion}");
    }
}
