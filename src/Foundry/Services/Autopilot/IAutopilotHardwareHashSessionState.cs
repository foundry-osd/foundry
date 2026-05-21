using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;

namespace Foundry.Services.Autopilot;

/// <summary>
/// Stores Autopilot hardware hash state that is valid only for the current app session.
/// </summary>
public interface IAutopilotHardwareHashSessionState
{
    /// <summary>
    /// Gets or sets whether the tenant was connected during the current app session.
    /// </summary>
    bool HasConnectedTenant { get; set; }

    /// <summary>
    /// Gets or sets the last tenant onboarding status from the current app session.
    /// </summary>
    AutopilotTenantOnboardingStatus? TenantOnboardingStatus { get; set; }

    /// <summary>
    /// Gets or sets the session-only PFX selected for boot media generation.
    /// </summary>
    AutopilotBootMediaCertificateSettings BootMediaCertificate { get; set; }

    /// <summary>
    /// Gets or sets the certificate credentials discovered from Graph in the current app session.
    /// </summary>
    IReadOnlyList<AutopilotGraphKeyCredential> Certificates { get; set; }

    /// <summary>
    /// Clears tenant, certificate table, and boot media PFX state without touching persisted settings.
    /// </summary>
    void ClearTenantConnection();
}
