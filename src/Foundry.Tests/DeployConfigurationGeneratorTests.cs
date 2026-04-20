using Foundry.Models.Configuration;
using Foundry.Services.Configuration;

namespace Foundry.Tests;

public sealed class DeployConfigurationGeneratorTests
{
    [Fact]
    public void Generate_WhenMachineNamingIsDisabled_ClearsPrefixAndResolvesDefaultAutopilotFolder()
    {
        var generator = new DeployConfigurationGenerator();
        var document = new FoundryExpertConfigurationDocument
        {
            Localization = new LocalizationSettings
            {
                VisibleLanguageCodes = [" fr_fr ", "EN-us", "fr-FR", ""],
                DefaultLanguageCodeOverride = "FR-fr",
                DefaultTimeZoneId = "Romance Standard Time",
                ForceSingleVisibleLanguage = true
            },
            Customization = new CustomizationSettings
            {
                MachineNaming = new MachineNamingSettings
                {
                    IsEnabled = false,
                    Prefix = "FD-",
                    AutoGenerateName = true,
                    AllowManualSuffixEdit = false
                }
            },
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

        Assert.Equal(["fr-FR", "en-US"], result.Localization.VisibleLanguageCodes);
        Assert.Equal("fr-FR", result.Localization.DefaultLanguageCodeOverride);
        Assert.Equal("Romance Standard Time", result.Localization.DefaultTimeZoneId);
        Assert.True(result.Localization.ForceSingleVisibleLanguage);
        Assert.False(result.Customization.MachineNaming.IsEnabled);
        Assert.Null(result.Customization.MachineNaming.Prefix);
        Assert.False(result.Customization.MachineNaming.AutoGenerateName);
        Assert.True(result.Customization.MachineNaming.AllowManualSuffixEdit);
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
