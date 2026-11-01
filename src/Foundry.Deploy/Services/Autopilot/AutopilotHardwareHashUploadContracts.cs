// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Autopilot;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Uploads a captured Autopilot hardware hash through the WinPE certificate-based Graph path.
/// </summary>
public interface IAutopilotHardwareHashUploadService
{
    Task<AutopilotHardwareHashUploadResult> UploadAsync(
        AutopilotHardwareHashUploadRequest request,
        IProgress<AutopilotHardwareHashUploadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record AutopilotHardwareHashUploadRequest
{
    public required DeployAutopilotHardwareHashUploadSettings Settings { get; init; }
    public required AutopilotHardwareHashDeviceIdentity Identity { get; init; }
    public required string WorkspaceRootPath { get; init; }
    public required string DiagnosticsRootPath { get; init; }
    /// <summary>
    /// Final deployment computer name to assign after import, or null to preserve the Autopilot name.
    /// </summary>
    public string? AssignedComputerName { get; init; }
}
