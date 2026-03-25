using System.Text.Json;
using Foundry.Models.Configuration;
using Foundry.Models.Configuration.Deploy;

namespace Foundry.Services.Configuration;

public sealed class DeployConfigurationGenerator : IDeployConfigurationGenerator
{
    public FoundryDeployConfigurationDocument Generate(FoundryExpertConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new FoundryDeployConfigurationDocument
        {
            Localization = new DeployLocalizationSettings
            {
                VisibleLanguageCodes = document.Localization.VisibleLanguageCodes,
                DefaultLanguageCodeOverride = document.Localization.DefaultLanguageCodeOverride,
                DefaultTimeZoneId = document.Localization.DefaultTimeZoneId,
                ForceSingleVisibleLanguage = document.Localization.ForceSingleVisibleLanguage
            },
            Customization = new DeployCustomizationSettings
            {
                MachineNaming = new DeployMachineNamingSettings
                {
                    IsEnabled = document.Customization.MachineNaming.IsEnabled,
                    Prefix = document.Customization.MachineNaming.IsEnabled
                        ? document.Customization.MachineNaming.Prefix
                        : null,
                    AutoGenerateName = document.Customization.MachineNaming.IsEnabled &&
                                       document.Customization.MachineNaming.AutoGenerateName,
                    AllowManualSuffixEdit = !document.Customization.MachineNaming.IsEnabled ||
                                            document.Customization.MachineNaming.AllowManualSuffixEdit
                }
            },
            Autopilot = new DeployAutopilotSettings
            {
                IsEnabled = document.Autopilot.IsEnabled,
                DefaultProfileFolderName = document.Autopilot.Profiles
                    .FirstOrDefault(profile => string.Equals(profile.Id, document.Autopilot.DefaultProfileId, StringComparison.OrdinalIgnoreCase))
                    ?.FolderName
            }
        };
    }

    public string Serialize(FoundryDeployConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, ConfigurationJsonDefaults.SerializerOptions);
    }
}
