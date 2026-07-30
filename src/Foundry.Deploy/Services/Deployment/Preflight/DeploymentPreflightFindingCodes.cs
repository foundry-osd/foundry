// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment.Preflight;

/// <summary>
/// Provides stable identifiers for Windows 11 deployment readiness findings.
/// </summary>
public static class DeploymentPreflightFindingCodes
{
    public const string FirmwareMode = "firmware_mode";
    public const string ArchitectureMismatch = "architecture_mismatch";
    public const string TpmMissing = "tpm_missing";
    public const string TpmVersion = "tpm_version";
    public const string TpmDisabled = "tpm_disabled";
    public const string TpmDeactivated = "tpm_deactivated";
    public const string SecureBootDisabled = "secure_boot_disabled";
    public const string SecureBootUnavailable = "secure_boot_unavailable";
    public const string DiskSizeUnknown = "disk_size_unknown";
    public const string DiskBelowRecommendedSize = "disk_below_recommended_size";
}
