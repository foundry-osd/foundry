namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Identifies the active certificate credential selected on the managed Autopilot app registration.
/// </summary>
public sealed record AutopilotCertificateMetadata
{
    /// <summary>
    /// Gets the Microsoft Graph key credential identifier for the selected certificate.
    /// </summary>
    public string? KeyId { get; init; }

    /// <summary>
    /// Gets the certificate thumbprint used to validate the operator-provided PFX during media generation.
    /// </summary>
    public string? Thumbprint { get; init; }

    /// <summary>
    /// Gets the operator-facing certificate display name.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Gets the UTC expiration time after which media generation must reject hardware hash upload mode.
    /// </summary>
    public DateTimeOffset? ExpiresOnUtc { get; init; }
}
