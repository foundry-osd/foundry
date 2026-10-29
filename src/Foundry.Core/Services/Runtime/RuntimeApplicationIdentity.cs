// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Runtime;

/// <summary>
/// Identifies a boot-media runtime application to the shared startup diagnostics.
/// </summary>
/// <param name="ApplicationId">Stable application name used in the Bootstrap handshake and the log context, for example <c>Foundry.Connect</c>.</param>
/// <param name="TelemetryApplication">Telemetry application identifier reported with remote diagnostics.</param>
/// <param name="Version">Application version reported with remote diagnostics.</param>
public sealed record RuntimeApplicationIdentity(string ApplicationId, string TelemetryApplication, string Version)
{
    /// <summary>
    /// Gets the name of the minimal local evidence file written when the process terminates unexpectedly.
    /// </summary>
    public string TerminationFileName => $"{ApplicationId.Replace(".", string.Empty, StringComparison.Ordinal)}-terminated.txt";
}
