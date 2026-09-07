// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Provides the file-system roots required to capture an Autopilot hardware hash in WinPE.
/// </summary>
public sealed record AutopilotHardwareHashCaptureRequest
{
    /// <summary>The OA3 service is the PE path; full Windows uses the existing registration assistant.</summary>
    public bool IsWinPe { get; init; } = true;

    public WirelessAdapterPresence InternalWireless { get; init; } = WirelessAdapterPresence.Unknown;

    /// <summary>Requires qualification of the exact PE/OA3/PCPKsp architecture and build combination, not file existence.</summary>
    public bool QualifiedToolPair { get; init; }

    /// <summary>Requires native qualification of the device and its loaded hardware drivers.</summary>
    public bool QualifiedHardware { get; init; }

    /// <summary>
    /// Gets the root of the applied offline Windows image.
    /// </summary>
    public string TargetWindowsRootPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the active WinPE Windows root that receives PCPKsp.dll before OA3Tool runs.
    /// </summary>
    public string WinPeWindowsRootPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the active Foundry runtime root, normally X:\Foundry.
    /// </summary>
    public string WorkspaceRootPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the retained diagnostic artifact folder under the applied Windows image.
    /// </summary>
    public string DiagnosticsRootPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional group tag selected by the operator.
    /// </summary>
    public string? GroupTag { get; init; }
}
