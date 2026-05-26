using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Evaluates whether persistent Autopilot settings are complete enough to generate deployment media.
/// </summary>
public static class AutopilotConfigurationValidator
{
    /// <summary>
    /// Determines whether Autopilot settings are ready for media generation.
    /// </summary>
    /// <param name="settings">Autopilot settings to evaluate.</param>
    /// <param name="currentTimeUtc">Current UTC time used for certificate expiration checks.</param>
    /// <returns><see langword="true"/> when the selected Autopilot mode is ready for output.</returns>
    public static bool IsReady(AutopilotSettings settings, DateTimeOffset currentTimeUtc)
    {
        return Evaluate(settings, currentTimeUtc).IsReady;
    }

    /// <summary>
    /// Evaluates Autopilot media readiness and returns the precise blocking reason.
    /// </summary>
    /// <param name="settings">Autopilot settings to evaluate.</param>
    /// <param name="currentTimeUtc">Current UTC time used for certificate expiration checks.</param>
    /// <returns>The Autopilot validation result.</returns>
    public static AutopilotConfigurationValidationResult Evaluate(AutopilotSettings settings, DateTimeOffset currentTimeUtc)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.IsEnabled)
        {
            return AutopilotConfigurationValidationResult.Ready(AutopilotConfigurationValidationCode.Disabled);
        }

        return settings.ProvisioningMode switch
        {
            AutopilotProvisioningMode.JsonProfile => GetSelectedJsonProfile(settings) is not null
                ? AutopilotConfigurationValidationResult.Ready(AutopilotConfigurationValidationCode.Ready)
                : AutopilotConfigurationValidationResult.Blocked(AutopilotConfigurationValidationCode.JsonProfileMissing),
            AutopilotProvisioningMode.HardwareHashUpload => EvaluateHardwareHashUpload(settings.HardwareHashUpload, currentTimeUtc),
            _ => AutopilotConfigurationValidationResult.Blocked(AutopilotConfigurationValidationCode.UnsupportedProvisioningMode)
        };
    }

    internal static void ThrowIfNotReady(AutopilotSettings settings, DateTimeOffset currentTimeUtc)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.IsEnabled)
        {
            return;
        }

        if (settings.ProvisioningMode == AutopilotProvisioningMode.JsonProfile && GetSelectedJsonProfile(settings) is null)
        {
            throw new InvalidOperationException("Autopilot JSON profile mode requires a selected profile.");
        }

        if (settings.ProvisioningMode == AutopilotProvisioningMode.HardwareHashUpload &&
            !EvaluateHardwareHashUpload(settings.HardwareHashUpload, currentTimeUtc).IsReady)
        {
            throw new InvalidOperationException("Autopilot hardware hash upload mode requires complete tenant metadata and a validated unexpired PFX matching the active certificate.");
        }

        if (!Enum.IsDefined(settings.ProvisioningMode))
        {
            throw new InvalidOperationException($"Unsupported Autopilot provisioning mode '{settings.ProvisioningMode}'.");
        }
    }

    private static AutopilotProfileSettings? GetSelectedJsonProfile(AutopilotSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.DefaultProfileId))
        {
            return null;
        }

        return settings.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, settings.DefaultProfileId, StringComparison.OrdinalIgnoreCase));
    }

    private static AutopilotConfigurationValidationResult EvaluateHardwareHashUpload(
        AutopilotHardwareHashUploadSettings? settings,
        DateTimeOffset currentTimeUtc)
    {
        if (settings?.Tenant is null)
        {
            return AutopilotConfigurationValidationResult.Blocked(AutopilotConfigurationValidationCode.HardwareHashSettingsMissing);
        }

        if (string.IsNullOrWhiteSpace(settings.Tenant.TenantId))
        {
            return AutopilotConfigurationValidationResult.Blocked(AutopilotConfigurationValidationCode.HardwareHashTenantMissing);
        }

        if (string.IsNullOrWhiteSpace(settings.Tenant.ApplicationObjectId))
        {
            return AutopilotConfigurationValidationResult.Blocked(AutopilotConfigurationValidationCode.HardwareHashAppRegistrationMissing);
        }

        if (string.IsNullOrWhiteSpace(settings.Tenant.ClientId))
        {
            return AutopilotConfigurationValidationResult.Blocked(AutopilotConfigurationValidationCode.HardwareHashClientIdMissing);
        }

        if (string.IsNullOrWhiteSpace(settings.Tenant.ServicePrincipalObjectId))
        {
            return AutopilotConfigurationValidationResult.Blocked(AutopilotConfigurationValidationCode.HardwareHashServicePrincipalMissing);
        }

        AutopilotConfigurationValidationResult bootMediaResult = EvaluateBootMediaCertificate(settings, currentTimeUtc);
        if (!bootMediaResult.IsReady)
        {
            return bootMediaResult;
        }

        if (string.IsNullOrWhiteSpace(settings.ActiveCertificate?.KeyId))
        {
            return AutopilotConfigurationValidationResult.Blocked(AutopilotConfigurationValidationCode.HardwareHashActiveCertificateMissing);
        }

        if (string.IsNullOrWhiteSpace(settings.ActiveCertificate.Thumbprint))
        {
            return AutopilotConfigurationValidationResult.Blocked(AutopilotConfigurationValidationCode.HardwareHashActiveCertificateThumbprintMissing);
        }

        if (settings.ActiveCertificate.ExpiresOnUtc is not { } expiresOnUtc)
        {
            return AutopilotConfigurationValidationResult.Blocked(AutopilotConfigurationValidationCode.HardwareHashActiveCertificateExpirationMissing);
        }

        if (expiresOnUtc <= currentTimeUtc)
        {
            return AutopilotConfigurationValidationResult.Blocked(AutopilotConfigurationValidationCode.HardwareHashActiveCertificateExpired);
        }

        return AutopilotConfigurationValidationResult.Ready(AutopilotConfigurationValidationCode.Ready);
    }

    private static AutopilotConfigurationValidationResult EvaluateBootMediaCertificate(
        AutopilotHardwareHashUploadSettings settings,
        DateTimeOffset currentTimeUtc)
    {
        AutopilotBootMediaCertificateSettings bootMediaCertificate = settings.BootMediaCertificate;

        if (string.IsNullOrWhiteSpace(bootMediaCertificate.PfxPath))
        {
            return AutopilotConfigurationValidationResult.Blocked(AutopilotConfigurationValidationCode.HardwareHashBootMediaPfxMissing);
        }

        if (string.IsNullOrWhiteSpace(bootMediaCertificate.PfxPassword))
        {
            return AutopilotConfigurationValidationResult.Blocked(AutopilotConfigurationValidationCode.HardwareHashBootMediaPfxPasswordMissing);
        }

        if (string.IsNullOrWhiteSpace(bootMediaCertificate.ValidatedThumbprint))
        {
            return AutopilotConfigurationValidationResult.Blocked(AutopilotConfigurationValidationCode.HardwareHashBootMediaCertificateNotValidated);
        }

        if (!string.Equals(
                NormalizeThumbprint(settings.ActiveCertificate?.Thumbprint),
                NormalizeThumbprint(bootMediaCertificate.ValidatedThumbprint),
                StringComparison.OrdinalIgnoreCase))
        {
            return AutopilotConfigurationValidationResult.Blocked(AutopilotConfigurationValidationCode.HardwareHashBootMediaCertificateThumbprintMismatch);
        }

        if (bootMediaCertificate.ValidatedExpiresOnUtc is not { } expiresOnUtc)
        {
            return AutopilotConfigurationValidationResult.Blocked(AutopilotConfigurationValidationCode.HardwareHashBootMediaCertificateExpirationMissing);
        }

        return expiresOnUtc > currentTimeUtc
            ? AutopilotConfigurationValidationResult.Ready(AutopilotConfigurationValidationCode.Ready)
            : AutopilotConfigurationValidationResult.Blocked(AutopilotConfigurationValidationCode.HardwareHashBootMediaCertificateExpired);
    }

    private static string? NormalizeThumbprint(string? thumbprint)
    {
        string? normalized = thumbprint?.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized.ToUpperInvariant();
    }
}

/// <summary>
/// Describes Autopilot media readiness and the most specific blocking reason.
/// </summary>
public sealed record AutopilotConfigurationValidationResult
{
    /// <summary>
    /// Gets whether the selected Autopilot mode is ready for media generation.
    /// </summary>
    public bool IsReady { get; init; }

    /// <summary>
    /// Gets the readiness or blocking reason code.
    /// </summary>
    public AutopilotConfigurationValidationCode Code { get; init; }

    /// <summary>
    /// Creates a ready validation result.
    /// </summary>
    /// <param name="code">Ready code.</param>
    /// <returns>A ready validation result.</returns>
    public static AutopilotConfigurationValidationResult Ready(AutopilotConfigurationValidationCode code)
    {
        return new AutopilotConfigurationValidationResult
        {
            IsReady = true,
            Code = code
        };
    }

    /// <summary>
    /// Creates a blocked validation result.
    /// </summary>
    /// <param name="code">Blocking reason code.</param>
    /// <returns>A blocked validation result.</returns>
    public static AutopilotConfigurationValidationResult Blocked(AutopilotConfigurationValidationCode code)
    {
        return new AutopilotConfigurationValidationResult
        {
            IsReady = false,
            Code = code
        };
    }
}

/// <summary>
/// Identifies the Autopilot readiness status or blocking reason.
/// </summary>
public enum AutopilotConfigurationValidationCode
{
    /// <summary>Autopilot is ready for media generation.</summary>
    Ready,

    /// <summary>Autopilot is disabled.</summary>
    Disabled,

    /// <summary>The selected Autopilot provisioning mode is unsupported.</summary>
    UnsupportedProvisioningMode,

    /// <summary>JSON profile mode has no valid selected profile.</summary>
    JsonProfileMissing,

    /// <summary>Hardware hash settings are missing.</summary>
    HardwareHashSettingsMissing,

    /// <summary>The tenant ID is missing.</summary>
    HardwareHashTenantMissing,

    /// <summary>The managed app registration object ID is missing.</summary>
    HardwareHashAppRegistrationMissing,

    /// <summary>The managed app client ID is missing.</summary>
    HardwareHashClientIdMissing,

    /// <summary>The managed app service principal object ID is missing.</summary>
    HardwareHashServicePrincipalMissing,

    /// <summary>The active certificate key ID is missing.</summary>
    HardwareHashActiveCertificateMissing,

    /// <summary>The active certificate thumbprint is missing.</summary>
    HardwareHashActiveCertificateThumbprintMissing,

    /// <summary>The active certificate expiration is missing.</summary>
    HardwareHashActiveCertificateExpirationMissing,

    /// <summary>The active certificate is expired.</summary>
    HardwareHashActiveCertificateExpired,

    /// <summary>The boot media PFX file was not selected.</summary>
    HardwareHashBootMediaPfxMissing,

    /// <summary>The boot media PFX password is missing.</summary>
    HardwareHashBootMediaPfxPasswordMissing,

    /// <summary>The boot media PFX has not been validated.</summary>
    HardwareHashBootMediaCertificateNotValidated,

    /// <summary>The boot media PFX thumbprint does not match the active certificate.</summary>
    HardwareHashBootMediaCertificateThumbprintMismatch,

    /// <summary>The boot media PFX expiration is missing.</summary>
    HardwareHashBootMediaCertificateExpirationMissing,

    /// <summary>The boot media PFX certificate is expired.</summary>
    HardwareHashBootMediaCertificateExpired
}
