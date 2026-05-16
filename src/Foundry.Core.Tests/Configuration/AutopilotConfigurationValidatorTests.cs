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
    [InlineData("", "application-object-id", "client-id", "certificate-key-id", "ABCDEF123456")]
    [InlineData("tenant-id", "", "client-id", "certificate-key-id", "ABCDEF123456")]
    [InlineData("tenant-id", "application-object-id", "", "certificate-key-id", "ABCDEF123456")]
    [InlineData("tenant-id", "application-object-id", "client-id", "", "ABCDEF123456")]
    [InlineData("tenant-id", "application-object-id", "client-id", "certificate-key-id", "")]
    public void IsReady_WhenHardwareHashModeHasMissingRequiredMetadata_ReturnsFalse(
        string tenantId,
        string applicationObjectId,
        string clientId,
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
                ServicePrincipalObjectId = "service-principal-object-id"
            },
            ActiveCertificate = new AutopilotCertificateMetadata
            {
                KeyId = keyId,
                Thumbprint = thumbprint,
                DisplayName = "Foundry OSD Autopilot Registration",
                ExpiresOnUtc = expiration
            }
        };
    }
}
