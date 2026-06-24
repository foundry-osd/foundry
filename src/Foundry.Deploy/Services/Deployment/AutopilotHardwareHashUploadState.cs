// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Describes the non-secret runtime status of the Autopilot hardware hash upload workflow.
/// </summary>
public enum AutopilotHardwareHashUploadState
{
    /// <summary>
    /// Hardware hash upload is not selected for this deployment.
    /// </summary>
    NotPlanned,

    /// <summary>
    /// Hardware hash upload is selected and awaiting the late Autopilot provisioning step.
    /// </summary>
    Planned,

    /// <summary>
    /// Dry-run mode wrote a sanitized hardware hash upload manifest without calling Graph.
    /// </summary>
    DryRunPrepared,

    /// <summary>
    /// Hardware hash upload was skipped because the media certificate is expired.
    /// </summary>
    SkippedCertificateExpired,

    /// <summary>
    /// Hardware hash upload was skipped because required media metadata is unavailable.
    /// </summary>
    SkippedMissingConfiguration,

    /// <summary>
    /// Hardware hash capture failed before an upload could be attempted.
    /// </summary>
    CaptureFailed,

    /// <summary>
    /// Graph import failed after a hash was captured.
    /// </summary>
    UploadFailed,

    /// <summary>
    /// Graph import succeeded but a required Autopilot confirmation wait reached the timeout.
    /// </summary>
    UploadTimedOut,

    /// <summary>
    /// The uploaded device appeared in Windows Autopilot devices.
    /// </summary>
    Completed
}
