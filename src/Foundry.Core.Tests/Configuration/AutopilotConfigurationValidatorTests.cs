using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class AutopilotConfigurationValidatorTests
{
    private static readonly DateTimeOffset EvaluationTimeUtc = new(2026, 5, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IsReady_WhenAutopilotIsDisabled_ReturnsTrueWithoutProfileOrHashSettings()
    {
        var settings = new AutopilotSettings
        {
            IsEnabled = false,
            ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload
        };

        Assert.True(AutopilotConfigurationValidator.IsReady(settings, EvaluationTimeUtc));
    }

    [Fact]
    public void IsReady_WhenJsonProfileModeHasSelectedProfile_ReturnsTrue()
    {
        var settings = new AutopilotSettings
        {
            IsEnabled = true,
            ProvisioningMode = AutopilotProvisioningMode.JsonProfile,
            DefaultProfileId = "profile-a",
            Profiles = [CreateProfile("profile-a")]
        };

        Assert.True(AutopilotConfigurationValidator.IsReady(settings, EvaluationTimeUtc));
    }

    [Fact]
    public void IsReady_WhenJsonProfileModeHasNoSelectedProfile_ReturnsFalse()
    {
        var settings = new AutopilotSettings
        {
            IsEnabled = true,
            ProvisioningMode = AutopilotProvisioningMode.JsonProfile
        };

        Assert.False(AutopilotConfigurationValidator.IsReady(settings, EvaluationTimeUtc));
    }

    [Fact]
    public void IsReady_WhenHardwareHashModeHasCompleteUnexpiredSettings_ReturnsTrue()
    {
        var settings = new AutopilotSettings
        {
            IsEnabled = true,
            ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
            HardwareHashUpload = CreateCompleteHardwareHashSettings(EvaluationTimeUtc.AddMonths(6))
        };

        Assert.True(AutopilotConfigurationValidator.IsReady(settings, EvaluationTimeUtc));
    }

    [Theory]
    [InlineData("", "application-object-id", "client-id", "service-principal-object-id", "certificate-key-id", "ABCDEF123456")]
    [InlineData("tenant-id", "", "client-id", "service-principal-object-id", "certificate-key-id", "ABCDEF123456")]
    [InlineData("tenant-id", "application-object-id", "", "service-principal-object-id", "certificate-key-id", "ABCDEF123456")]
    [InlineData("tenant-id", "application-object-id", "client-id", "", "certificate-key-id", "ABCDEF123456")]
    [InlineData("tenant-id", "application-object-id", "client-id", "service-principal-object-id", "", "ABCDEF123456")]
    [InlineData("tenant-id", "application-object-id", "client-id", "service-principal-object-id", "certificate-key-id", "")]
    public void IsReady_WhenHardwareHashModeHasMissingRequiredMetadata_ReturnsFalse(
        string tenantId,
        string applicationObjectId,
        string clientId,
        string servicePrincipalObjectId,
        string keyId,
        string thumbprint)
    {
        var settings = new AutopilotSettings
        {
            IsEnabled = true,
            ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
            HardwareHashUpload = CreateCompleteHardwareHashSettings(
                EvaluationTimeUtc.AddMonths(6),
                tenantId,
                applicationObjectId,
                clientId,
                servicePrincipalObjectId,
                keyId,
                thumbprint)
        };

        Assert.False(AutopilotConfigurationValidator.IsReady(settings, EvaluationTimeUtc));
    }

    [Fact]
    public void IsReady_WhenHardwareHashCertificateIsExpired_ReturnsFalse()
    {
        var settings = new AutopilotSettings
        {
            IsEnabled = true,
            ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
            HardwareHashUpload = CreateCompleteHardwareHashSettings(EvaluationTimeUtc.AddTicks(-1))
        };

        Assert.False(AutopilotConfigurationValidator.IsReady(settings, EvaluationTimeUtc));
    }

    [Fact]
    public void Evaluate_WhenHardwareHashCertificateIsExpired_ReturnsCertificateExpired()
    {
        var settings = new AutopilotSettings
        {
            IsEnabled = true,
            ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
            HardwareHashUpload = CreateCompleteHardwareHashSettings(EvaluationTimeUtc.AddTicks(-1)) with
            {
                BootMediaCertificate = new AutopilotBootMediaCertificateSettings
                {
                    PfxPath = @"E:\Secrets\foundry-osd-autopilot-registration.pfx",
                    PfxPassword = "correct-password",
                    ValidatedThumbprint = "ABCDEF123456",
                    ValidatedExpiresOnUtc = EvaluationTimeUtc.AddMonths(6)
                }
            }
        };

        AutopilotConfigurationValidationResult result = AutopilotConfigurationValidator.Evaluate(settings, EvaluationTimeUtc);

        Assert.False(result.IsReady);
        Assert.Equal(AutopilotConfigurationValidationCode.HardwareHashActiveCertificateExpired, result.Code);
    }

    [Fact]
    public void Evaluate_WhenHardwareHashBootMediaCertificateIsExpired_ReturnsBootMediaCertificateExpired()
    {
        var settings = new AutopilotSettings
        {
            IsEnabled = true,
            ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
            HardwareHashUpload = CreateCompleteHardwareHashSettings(EvaluationTimeUtc.AddMonths(6)) with
            {
                BootMediaCertificate = new AutopilotBootMediaCertificateSettings
                {
                    PfxPath = @"E:\Secrets\foundry-osd-autopilot-registration.pfx",
                    PfxPassword = "correct-password",
                    ValidatedThumbprint = "ABCDEF123456",
                    ValidatedExpiresOnUtc = EvaluationTimeUtc.AddTicks(-1)
                }
            }
        };

        AutopilotConfigurationValidationResult result = AutopilotConfigurationValidator.Evaluate(settings, EvaluationTimeUtc);

        Assert.False(result.IsReady);
        Assert.Equal(AutopilotConfigurationValidationCode.HardwareHashBootMediaCertificateExpired, result.Code);
    }

    [Fact]
    public void IsReady_WhenHardwareHashBootMediaCertificateIsMissing_ReturnsFalse()
    {
        var settings = new AutopilotSettings
        {
            IsEnabled = true,
            ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
            HardwareHashUpload = CreateCompleteHardwareHashSettings(EvaluationTimeUtc.AddMonths(6)) with
            {
                BootMediaCertificate = new AutopilotBootMediaCertificateSettings()
            }
        };

        Assert.False(AutopilotConfigurationValidator.IsReady(settings, EvaluationTimeUtc));
    }

    [Fact]
    public void Evaluate_WhenHardwareHashBootMediaCertificateIsMissing_ReturnsPfxMissing()
    {
        var settings = new AutopilotSettings
        {
            IsEnabled = true,
            ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
            HardwareHashUpload = CreateCompleteHardwareHashSettings(EvaluationTimeUtc.AddMonths(6)) with
            {
                BootMediaCertificate = new AutopilotBootMediaCertificateSettings()
            }
        };

        AutopilotConfigurationValidationResult result = AutopilotConfigurationValidator.Evaluate(settings, EvaluationTimeUtc);

        Assert.False(result.IsReady);
        Assert.Equal(AutopilotConfigurationValidationCode.HardwareHashBootMediaPfxMissing, result.Code);
    }

    [Fact]
    public void Evaluate_WhenHardwareHashHasNoActiveCertificateOrPfx_ReturnsPfxMissing()
    {
        var settings = new AutopilotSettings
        {
            IsEnabled = true,
            ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
            HardwareHashUpload = CreateCompleteHardwareHashSettings(EvaluationTimeUtc.AddMonths(6)) with
            {
                ActiveCertificate = null,
                BootMediaCertificate = new AutopilotBootMediaCertificateSettings()
            }
        };

        AutopilotConfigurationValidationResult result = AutopilotConfigurationValidator.Evaluate(settings, EvaluationTimeUtc);

        Assert.False(result.IsReady);
        Assert.Equal(AutopilotConfigurationValidationCode.HardwareHashBootMediaPfxMissing, result.Code);
    }

    [Fact]
    public void Evaluate_WhenHardwareHashBootMediaThumbprintDoesNotMatch_ReturnsThumbprintMismatch()
    {
        var settings = new AutopilotSettings
        {
            IsEnabled = true,
            ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
            HardwareHashUpload = CreateCompleteHardwareHashSettings(EvaluationTimeUtc.AddMonths(6)) with
            {
                BootMediaCertificate = new AutopilotBootMediaCertificateSettings
                {
                    PfxPath = @"E:\Secrets\foundry-osd-autopilot-registration.pfx",
                    PfxPassword = "correct-password",
                    ValidatedThumbprint = "9876543210",
                    ValidatedExpiresOnUtc = EvaluationTimeUtc.AddMonths(6)
                }
            }
        };

        AutopilotConfigurationValidationResult result = AutopilotConfigurationValidator.Evaluate(settings, EvaluationTimeUtc);

        Assert.False(result.IsReady);
        Assert.Equal(AutopilotConfigurationValidationCode.HardwareHashBootMediaCertificateThumbprintMismatch, result.Code);
    }

    [Fact]
    public void IsReady_WhenProvisioningModeIsUnsupported_ReturnsFalse()
    {
        var settings = new AutopilotSettings
        {
            IsEnabled = true,
            ProvisioningMode = (AutopilotProvisioningMode)999
        };

        Assert.False(AutopilotConfigurationValidator.IsReady(settings, EvaluationTimeUtc));
    }

    [Fact]
    public void IsReady_WhenHardwareHashUploadSettingsAreNull_ReturnsFalse()
    {
        var settings = new AutopilotSettings
        {
            IsEnabled = true,
            ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
            HardwareHashUpload = null!
        };

        Assert.False(AutopilotConfigurationValidator.IsReady(settings, EvaluationTimeUtc));
    }

    [Fact]
    public void IsReady_WhenHardwareHashTenantSettingsAreNull_ReturnsFalse()
    {
        var settings = new AutopilotSettings
        {
            IsEnabled = true,
            ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
            HardwareHashUpload = CreateCompleteHardwareHashSettings(EvaluationTimeUtc.AddMonths(6)) with
            {
                Tenant = null!
            }
        };

        Assert.False(AutopilotConfigurationValidator.IsReady(settings, EvaluationTimeUtc));
    }

    [Fact]
    public void ThrowIfNotReady_WhenProvisioningModeIsUnsupported_ThrowsInvalidOperationException()
    {
        var settings = new AutopilotSettings
        {
            IsEnabled = true,
            ProvisioningMode = (AutopilotProvisioningMode)999
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => AutopilotConfigurationValidator.ThrowIfNotReady(settings, EvaluationTimeUtc));
        Assert.Contains("unsupported", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static AutopilotProfileSettings CreateProfile(string id)
    {
        return new AutopilotProfileSettings
        {
            Id = id,
            DisplayName = id,
            FolderName = id,
            Source = "import",
            ImportedAtUtc = EvaluationTimeUtc,
            JsonContent = "{}"
        };
    }

    private static AutopilotHardwareHashUploadSettings CreateCompleteHardwareHashSettings(
        DateTimeOffset expiration,
        string tenantId = "tenant-id",
        string applicationObjectId = "application-object-id",
        string clientId = "client-id",
        string servicePrincipalObjectId = "service-principal-object-id",
        string keyId = "certificate-key-id",
        string thumbprint = "ABCDEF123456")
    {
        return new AutopilotHardwareHashUploadSettings
        {
            Tenant = new AutopilotTenantRegistrationSettings
            {
                TenantId = tenantId,
                ApplicationObjectId = applicationObjectId,
                ClientId = clientId,
                ServicePrincipalObjectId = servicePrincipalObjectId
            },
            ActiveCertificate = new AutopilotCertificateMetadata
            {
                KeyId = keyId,
                Thumbprint = thumbprint,
                DisplayName = "Foundry OSD Autopilot Registration",
                ExpiresOnUtc = expiration
            },
            BootMediaCertificate = new AutopilotBootMediaCertificateSettings
            {
                PfxPath = @"E:\Secrets\foundry-osd-autopilot-registration.pfx",
                PfxPassword = "correct-password",
                ValidatedThumbprint = thumbprint,
                ValidatedExpiresOnUtc = expiration
            }
        };
    }
}
