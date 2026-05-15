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
}
