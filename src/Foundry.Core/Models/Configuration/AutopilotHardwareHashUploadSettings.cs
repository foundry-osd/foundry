// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Stores persistent metadata for WinPE Autopilot hardware hash upload without private key material.
/// </summary>
public sealed record AutopilotHardwareHashUploadSettings
{
    /// <summary>
    /// Gets the display name used for the managed Foundry app registration in Microsoft Entra ID.
    /// </summary>
    public const string ManagedAppRegistrationDisplayName = "Foundry OSD Autopilot Registration";

    /// <summary>
    /// Gets the tenant and managed app registration identities.
    /// </summary>
    public AutopilotTenantRegistrationSettings Tenant { get; init; } = new();

    /// <summary>
    /// Gets the currently selected app registration certificate credential metadata.
    /// </summary>
    public AutopilotCertificateMetadata? ActiveCertificate { get; init; }

    /// <summary>
    /// Gets the group tags discovered from Intune for default selection in Foundry.Deploy.
    /// </summary>
    public IReadOnlyList<string> KnownGroupTags { get; init; } = [];

    /// <summary>
    /// Gets the default Autopilot group tag selected during media generation.
    /// </summary>
    public string? DefaultGroupTag { get; init; }

    /// <summary>
    /// Gets the PFX selected for boot media generation and its last validation result.
    /// </summary>
    public AutopilotBootMediaCertificateSettings BootMediaCertificate { get; init; } = new();
}

/// <summary>
/// Identifies the Autopilot certificate staged on boot media; its password stays in the session secret state.
/// </summary>
public sealed record AutopilotBootMediaCertificateSettings
{
    /// <summary>
    /// Gets the operator-selected password-protected PFX held in the authoring file library.
    /// </summary>
    public AuthoringFileReference? Pfx { get; init; }

    /// <summary>
    /// Gets the validated PFX leaf certificate thumbprint.
    /// </summary>
    public string? ValidatedThumbprint { get; init; }

    /// <summary>
    /// Gets the validated PFX leaf certificate expiration.
    /// </summary>
    public DateTimeOffset? ValidatedExpiresOnUtc { get; init; }
}
