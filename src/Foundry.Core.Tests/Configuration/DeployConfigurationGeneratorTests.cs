using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Telemetry;

namespace Foundry.Core.Tests.Configuration;

public sealed class DeployConfigurationGeneratorTests
{
    [Fact]
    public void Generate_WhenMachineNamingIsDisabled_ClearsPrefixAndKeepsManualSuffixEditable()
    {
        var generator = new DeployConfigurationGenerator();
        var document = new FoundryExpertConfigurationDocument
        {
            Customization = new CustomizationSettings
            {
                MachineNaming = new MachineNamingSettings
                {
                    IsEnabled = false,
                    Prefix = "FD-",
                    AutoGenerateName = true,
                    AllowManualSuffixEdit = false
                }
            }
        };

        var result = generator.Generate(document);

        Assert.False(result.Customization.MachineNaming.IsEnabled);
        Assert.Null(result.Customization.MachineNaming.Prefix);
        Assert.False(result.Customization.MachineNaming.AutoGenerateName);
        Assert.True(result.Customization.MachineNaming.AllowManualSuffixEdit);
    }

    [Fact]
    public void Generate_PropagatesTelemetrySettings()
    {
        var generator = new DeployConfigurationGenerator();
        var telemetry = new TelemetrySettings
        {
            IsEnabled = false,
            InstallId = "install-id",
            HostUrl = TelemetryDefaults.PostHogEuHost,
            ProjectToken = "project-token",
            RuntimePayloadSource = TelemetryRuntimePayloadSources.Release
        };

        var result = generator.Generate(new FoundryExpertConfigurationDocument { Telemetry = telemetry });

        Assert.Same(telemetry, result.Telemetry);
    }

    [Fact]
    public void Generate_CanonicalizesVisibleLanguagesAndDropsMissingDefaultOverride()
    {
        var generator = new DeployConfigurationGenerator();
        var document = new FoundryExpertConfigurationDocument
        {
            Localization = new LocalizationSettings
            {
                VisibleLanguageCodes = [" fr_fr ", "EN-us", "fr-FR", ""],
                DefaultLanguageCodeOverride = "de-DE",
                DefaultTimeZoneId = "Romance Standard Time",
                ForceSingleVisibleLanguage = true
            }
        };

        var result = generator.Generate(document);

        Assert.Equal(["fr-FR", "en-US"], result.Localization.VisibleLanguageCodes);
        Assert.Null(result.Localization.DefaultLanguageCodeOverride);
        Assert.Equal("Romance Standard Time", result.Localization.DefaultTimeZoneId);
        Assert.True(result.Localization.ForceSingleVisibleLanguage);
    }

    [Fact]
    public void Serialize_WhenDefaultTimeZoneIdIsSet_WritesCamelCaseProperty()
    {
        var generator = new DeployConfigurationGenerator();
        var document = generator.Generate(new FoundryExpertConfigurationDocument
        {
            Localization = new LocalizationSettings
            {
                DefaultTimeZoneId = "Romance Standard Time"
            }
        });

        string json = generator.Serialize(document);

        Assert.Contains("\"defaultTimeZoneId\": \"Romance Standard Time\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ResolvesDefaultAutopilotProfileFolder()
    {
        var generator = new DeployConfigurationGenerator();
        var document = new FoundryExpertConfigurationDocument
        {
            Autopilot = new AutopilotSettings
            {
                IsEnabled = true,
                DefaultProfileId = "profile-b",
                Profiles =
                [
                    CreateProfile("profile-a", "profile-a-folder"),
                    CreateProfile("profile-b", "profile-b-folder")
                ]
            }
        };

        var result = generator.Generate(document);

        Assert.True(result.Autopilot.IsEnabled);
        Assert.Equal("profile-b-folder", result.Autopilot.DefaultProfileFolderName);
    }

    [Fact]
    public void Generate_WhenJsonProfileModeHasNoSelectedProfile_ThrowsInvalidOperationException()
    {
        var generator = new DeployConfigurationGenerator();
        var document = new FoundryExpertConfigurationDocument
        {
            Autopilot = new AutopilotSettings
            {
                IsEnabled = true,
                ProvisioningMode = AutopilotProvisioningMode.JsonProfile
            }
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => generator.Generate(document));
        Assert.Contains("JSON", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_WhenHardwareHashModeIsEnabled_DoesNotRequireSelectedProfile()
    {
        var generator = new DeployConfigurationGenerator();
        DateTimeOffset expiration = DateTimeOffset.UtcNow.AddMonths(6);
        var document = new FoundryExpertConfigurationDocument
        {
            Autopilot = new AutopilotSettings
            {
                IsEnabled = true,
                ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
                HardwareHashUpload = CreateCompleteHardwareHashSettings(expiration)
            }
        };

        var result = generator.Generate(document);

        Assert.True(result.Autopilot.IsEnabled);
        Assert.Equal(AutopilotProvisioningMode.HardwareHashUpload, result.Autopilot.ProvisioningMode);
        Assert.Null(result.Autopilot.DefaultProfileFolderName);
        Assert.Equal("tenant-id", result.Autopilot.HardwareHashUpload.TenantId);
        Assert.Equal("client-id", result.Autopilot.HardwareHashUpload.ClientId);
        Assert.Equal("certificate-key-id", result.Autopilot.HardwareHashUpload.ActiveCertificateKeyId);
        Assert.Equal("ABCDEF123456", result.Autopilot.HardwareHashUpload.ActiveCertificateThumbprint);
        Assert.Equal(expiration, result.Autopilot.HardwareHashUpload.ActiveCertificateExpiresOnUtc);
        Assert.Equal("Sales", result.Autopilot.HardwareHashUpload.DefaultGroupTag);
    }

    [Fact]
    public void Generate_WhenHardwareHashCertificateIsExpired_ThrowsInvalidOperationException()
    {
        var generator = new DeployConfigurationGenerator();
        var document = new FoundryExpertConfigurationDocument
        {
            Autopilot = new AutopilotSettings
            {
                IsEnabled = true,
                ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
                HardwareHashUpload = CreateCompleteHardwareHashSettings(DateTimeOffset.UtcNow.AddDays(-1))
            }
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => generator.Generate(document));
        Assert.Contains("certificate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_WhenAutopilotIsDisabledWithNullHardwareHashSettings_DoesNotThrow()
    {
        var generator = new DeployConfigurationGenerator();
        var document = new FoundryExpertConfigurationDocument
        {
            Autopilot = new AutopilotSettings
            {
                IsEnabled = false,
                HardwareHashUpload = null!
            }
        };

        var result = generator.Generate(document);

        Assert.False(result.Autopilot.IsEnabled);
        Assert.Equal(AutopilotProvisioningMode.JsonProfile, result.Autopilot.ProvisioningMode);
        Assert.NotNull(result.Autopilot.HardwareHashUpload);
    }

    [Fact]
    public void Generate_WhenProvisioningModeIsUnsupported_ThrowsInvalidOperationException()
    {
        var generator = new DeployConfigurationGenerator();
        var document = new FoundryExpertConfigurationDocument
        {
            Autopilot = new AutopilotSettings
            {
                IsEnabled = true,
                ProvisioningMode = (AutopilotProvisioningMode)999
            }
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => generator.Generate(document));
        Assert.Contains("unsupported", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static AutopilotProfileSettings CreateProfile(string id, string folderName)
    {
        return new AutopilotProfileSettings
        {
            Id = id,
            DisplayName = id,
            FolderName = folderName,
            Source = "import",
            ImportedAtUtc = DateTimeOffset.UtcNow,
            JsonContent = "{}"
        };
    }

    private static AutopilotHardwareHashUploadSettings CreateCompleteHardwareHashSettings(DateTimeOffset expiration)
    {
        return new AutopilotHardwareHashUploadSettings
        {
            Tenant = new AutopilotTenantRegistrationSettings
            {
                TenantId = "tenant-id",
                ApplicationObjectId = "application-object-id",
                ClientId = "client-id",
                ServicePrincipalObjectId = "service-principal-object-id"
            },
            ActiveCertificate = new AutopilotCertificateMetadata
            {
                KeyId = "certificate-key-id",
                Thumbprint = "ABCDEF123456",
                DisplayName = "Foundry OSD Autopilot Registration",
                ExpiresOnUtc = expiration
            },
            KnownGroupTags = ["Sales", "Engineering"],
            DefaultGroupTag = "Sales"
        };
    }
}
