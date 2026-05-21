using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;

namespace Foundry.Services.Autopilot;

/// <summary>
/// Coordinates interactive tenant onboarding for Autopilot hardware hash upload.
/// </summary>
public interface IAutopilotTenantOnboardingService
{
    /// <summary>
    /// Connects to Microsoft Graph, creates or reuses the managed app registration, and returns the updated settings.
    /// </summary>
    /// <param name="currentSettings">Current persisted hardware hash upload settings.</param>
    /// <param name="cancellationToken">Token that cancels Graph requests.</param>
    /// <returns>The tenant onboarding result and sanitized settings to persist.</returns>
    Task<AutopilotTenantOnboardingResult> ConnectAsync(
        AutopilotHardwareHashUploadSettings currentSettings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a password-protected PFX certificate, adds its public credential to the managed app, and selects it as active.
    /// </summary>
    /// <param name="currentSettings">Current persisted hardware hash upload settings.</param>
    /// <param name="pfxOutputPath">Operator-selected PFX output path.</param>
    /// <param name="validityMonths">Certificate validity duration in months.</param>
    /// <param name="cancellationToken">Token that cancels Graph requests.</param>
    /// <returns>The certificate creation result and updated persistent settings.</returns>
    Task<AutopilotCertificateCreationResult> CreateCertificateAsync(
        AutopilotHardwareHashUploadSettings currentSettings,
        string pfxOutputPath,
        int validityMonths,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the selected certificate credential from the managed app registration.
    /// </summary>
    /// <param name="currentSettings">Current persisted hardware hash upload settings.</param>
    /// <param name="certificateKeyId">Microsoft Graph key credential identifier to remove.</param>
    /// <param name="cancellationToken">Token that cancels authentication and Graph requests.</param>
    /// <returns>Updated settings and app registration certificate credentials.</returns>
    Task<AutopilotCertificateRemovalResult> RemoveCertificateAsync(
        AutopilotHardwareHashUploadSettings currentSettings,
        string certificateKeyId,
        CancellationToken cancellationToken = default);
}
