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
}
