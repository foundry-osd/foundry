// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

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
}

public sealed record AutopilotHardwareHashUploadProgress(
    string Message,
    string? Detail = null,
    bool IsIndeterminate = true);

public sealed record AutopilotHardwareHashUploadResult
{
    public required AutopilotHardwareHashUploadState State { get; init; }
    public required string Message { get; init; }
    public string? FailureCode { get; init; }
    public string? ImportId { get; init; }
    public string? ImportedIdentityId { get; init; }
    public string? AutopilotDeviceId { get; init; }
    public string? ArtifactPath { get; init; }

    public bool IsCompleted => State == AutopilotHardwareHashUploadState.Completed;

    public static AutopilotHardwareHashUploadResult Completed(
        string message,
        string? importId = null,
        string? importedIdentityId = null,
        string? autopilotDeviceId = null)
    {
        return new AutopilotHardwareHashUploadResult
        {
            State = AutopilotHardwareHashUploadState.Completed,
            Message = message,
            ImportId = importId,
            ImportedIdentityId = importedIdentityId,
            AutopilotDeviceId = autopilotDeviceId
        };
    }

    public static AutopilotHardwareHashUploadResult Failed(
        AutopilotHardwareHashUploadState state,
        string message,
        string? failureCode = null,
        string? importId = null,
        string? importedIdentityId = null,
        string? autopilotDeviceId = null)
    {
        return new AutopilotHardwareHashUploadResult
        {
            State = state,
            Message = message,
            FailureCode = failureCode,
            ImportId = importId,
            ImportedIdentityId = importedIdentityId,
            AutopilotDeviceId = autopilotDeviceId
        };
    }
}
