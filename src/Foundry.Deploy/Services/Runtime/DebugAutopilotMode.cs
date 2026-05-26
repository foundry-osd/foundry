namespace Foundry.Deploy.Services.Runtime;

/// <summary>
/// Selects the in-memory Autopilot scenario used by Foundry.Deploy debug safe mode.
/// </summary>
public enum DebugAutopilotMode
{
    /// <summary>
    /// Disables Autopilot during the debug deployment run.
    /// </summary>
    None,

    /// <summary>
    /// Simulates JSON profile provisioning during the debug deployment run.
    /// </summary>
    JsonProfile,

    /// <summary>
    /// Simulates hardware hash upload provisioning with a valid certificate.
    /// </summary>
    HardwareHashUploadValidCertificate,

    /// <summary>
    /// Simulates hardware hash upload provisioning with an expired certificate.
    /// </summary>
    HardwareHashUploadExpiredCertificate,

    /// <summary>
    /// Simulates hardware hash upload provisioning with incomplete certificate metadata.
    /// </summary>
    HardwareHashUploadMissingCertificateMetadata,

    /// <summary>
    /// Simulates hardware hash upload provisioning without a default group tag.
    /// </summary>
    HardwareHashUploadNoDefaultGroupTag
}
