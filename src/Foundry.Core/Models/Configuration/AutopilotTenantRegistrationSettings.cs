namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Stores the tenant and managed app registration identities used for Autopilot hardware hash upload.
/// </summary>
public sealed record AutopilotTenantRegistrationSettings
{
    /// <summary>
    /// Gets the Microsoft Entra tenant ID used by the managed app registration.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Gets the application object ID for the managed app registration.
    /// </summary>
    public string? ApplicationObjectId { get; init; }

    /// <summary>
    /// Gets the application client ID used for certificate-based Graph authentication in WinPE.
    /// </summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// Gets the service principal object ID created for the managed app registration.
    /// </summary>
    public string? ServicePrincipalObjectId { get; init; }
}
