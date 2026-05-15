namespace Foundry.Deploy.Models.Configuration;

/// <summary>
/// Carries non-secret tenant and certificate metadata needed by the WinPE hardware hash upload workflow.
/// </summary>
public sealed record DeployAutopilotHardwareHashUploadSettings
{
    /// <summary>
    /// Gets the Microsoft Entra tenant ID used for Graph authentication.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Gets the application client ID used for certificate-based Graph authentication.
    /// </summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// Gets the selected app registration certificate credential identifier for diagnostics.
    /// </summary>
    public string? ActiveCertificateKeyId { get; init; }

    /// <summary>
    /// Gets the selected certificate thumbprint expected from the encrypted media certificate.
    /// </summary>
    public string? ActiveCertificateThumbprint { get; init; }

    /// <summary>
    /// Gets the selected certificate expiration time used to skip expired media without blocking OS deployment.
    /// </summary>
    public DateTimeOffset? ActiveCertificateExpiresOnUtc { get; init; }

    /// <summary>
    /// Gets the default group tag offered by Foundry.Deploy for hardware hash upload.
    /// </summary>
    public string? DefaultGroupTag { get; init; }
}
