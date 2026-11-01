// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Autopilot;

public sealed record AutopilotHardwareHashUploadProgress(
    string Message,
    string? Detail = null,
    bool IsIndeterminate = true);

public sealed record AutopilotHardwareHashUploadResult
{
    public required AutopilotHardwareHashUploadState State { get; init; }
    public required string Message { get; init; }
    public string? FailureCode { get; init; }

    /// <summary>
    /// Graph <c>deviceErrorName</c> reported by a failed import (for example <c>ZtdDeviceAssignedToOtherTenant</c>),
    /// when the failure came from the import state. Callers can map it to a specific user-facing message.
    /// </summary>
    public string? ImportErrorName { get; init; }
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
        string? autopilotDeviceId = null,
        string? importErrorName = null)
    {
        return new AutopilotHardwareHashUploadResult
        {
            State = state,
            Message = message,
            FailureCode = failureCode,
            ImportErrorName = importErrorName,
            ImportId = importId,
            ImportedIdentityId = importedIdentityId,
            AutopilotDeviceId = autopilotDeviceId
        };
    }
}
