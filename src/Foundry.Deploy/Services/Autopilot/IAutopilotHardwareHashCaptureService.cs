// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Captures an Autopilot hardware hash from WinPE without using PowerShell implementation logic.
/// </summary>
public interface IAutopilotHardwareHashCaptureService
{
    /// <summary>
    /// Copies required support files, runs OA3Tool, parses OA3.xml, and retains sanitized diagnostics.
    /// </summary>
    /// <param name="request">Capture file-system roots and optional group tag.</param>
    /// <param name="cancellationToken">Token that cancels file and process work.</param>
    /// <returns>The structured capture result.</returns>
    Task<AutopilotHardwareHashCaptureResult> CaptureAsync(
        AutopilotHardwareHashCaptureRequest request,
        CancellationToken cancellationToken = default);
}
