using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;

namespace Foundry.Services.Autopilot;

/// <summary>
/// Represents a created app registration certificate and the one-time generated PFX password.
/// </summary>
public sealed record AutopilotCertificateCreationResult
{
    /// <summary>
    /// Gets updated hardware hash upload settings with the created certificate selected as active.
    /// </summary>
    public AutopilotHardwareHashUploadSettings Settings { get; init; } = new();

    /// <summary>
    /// Gets the generated password for the exported PFX. This must not be persisted by Foundry.
    /// </summary>
    public string GeneratedPassword { get; init; } = string.Empty;

    /// <summary>
    /// Gets the certificate metadata selected as active after creation.
    /// </summary>
    public AutopilotCertificateMetadata Certificate { get; init; } = new();

    /// <summary>
    /// Gets the app registration certificate credentials after creation.
    /// </summary>
    public IReadOnlyList<AutopilotGraphKeyCredential> Certificates { get; init; } = [];
}
