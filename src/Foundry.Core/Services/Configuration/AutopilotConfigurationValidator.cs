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
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.IsEnabled)
        {
            return true;
        }

        return settings.ProvisioningMode switch
        {
            AutopilotProvisioningMode.JsonProfile => GetSelectedJsonProfile(settings) is not null,
            AutopilotProvisioningMode.HardwareHashUpload => IsHardwareHashUploadReady(settings.HardwareHashUpload, currentTimeUtc),
            _ => false
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
            !IsHardwareHashUploadReady(settings.HardwareHashUpload, currentTimeUtc))
        {
            throw new InvalidOperationException("Autopilot hardware hash upload mode requires complete tenant and unexpired certificate metadata.");
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

    private static bool IsHardwareHashUploadReady(
        AutopilotHardwareHashUploadSettings? settings,
        DateTimeOffset currentTimeUtc)
    {
        if (settings?.Tenant is null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(settings.Tenant.TenantId) &&
               !string.IsNullOrWhiteSpace(settings.Tenant.ApplicationObjectId) &&
               !string.IsNullOrWhiteSpace(settings.Tenant.ClientId) &&
               !string.IsNullOrWhiteSpace(settings.ActiveCertificate?.KeyId) &&
               !string.IsNullOrWhiteSpace(settings.ActiveCertificate.Thumbprint) &&
               settings.ActiveCertificate.ExpiresOnUtc is { } expiresOnUtc &&
               expiresOnUtc > currentTimeUtc;
    }
}
