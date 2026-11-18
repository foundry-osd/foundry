// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Telemetry;

/// <summary>
/// Defines the process that hosts an application, reported as <c>runtime_host</c> on the WinPE events.
/// </summary>
public static class TelemetryRuntimeHosts
{
    /// <summary>
    /// The single Foundry.WinPE executable: its guardian and its UI process, which runs the Foundry Connect and Foundry Deploy
    /// phases.
    /// </summary>
    public const string FoundryWinPe = "foundry_winpe";
}
