using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Generates the reduced Foundry.Deploy runtime configuration from Expert Deploy settings.
/// </summary>
public sealed class DeployConfigurationGenerator : IDeployConfigurationGenerator
{
    /// <inheritdoc />
    public FoundryDeployConfigurationDocument Generate(FoundryExpertConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        string[] visibleLanguageCodes = CanonicalizeLanguageCodes(document.Localization.VisibleLanguageCodes);
        string? defaultLanguageCodeOverride = CanonicalizeOptionalLanguageCode(document.Localization.DefaultLanguageCodeOverride);
        if (!visibleLanguageCodes.Any(code => LanguageCodeUtility.NormalizeForComparison(code).Equals(
                LanguageCodeUtility.NormalizeForComparison(defaultLanguageCodeOverride),
                StringComparison.OrdinalIgnoreCase)))
        {
            // The default language override is only valid when the language remains visible to the deployment UI.
            defaultLanguageCodeOverride = null;
        }

        return new FoundryDeployConfigurationDocument
        {
            Localization = new DeployLocalizationSettings
            {
                VisibleLanguageCodes = visibleLanguageCodes,
                DefaultLanguageCodeOverride = defaultLanguageCodeOverride,
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

    /// <inheritdoc />
    public string Serialize(FoundryDeployConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, ConfigurationJsonDefaults.SerializerOptions);
    }

    private static string[] CanonicalizeLanguageCodes(IEnumerable<string> languageCodes)
    {
        ArgumentNullException.ThrowIfNull(languageCodes);

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<string> result = [];
        foreach (string languageCode in languageCodes)
        {
            string canonicalCode = LanguageCodeUtility.Canonicalize(languageCode);
            if (string.IsNullOrWhiteSpace(canonicalCode) ||
                !seen.Add(LanguageCodeUtility.NormalizeForComparison(canonicalCode)))
            {
                continue;
            }

            result.Add(canonicalCode);
        }

        return result.ToArray();
    }

    private static string? CanonicalizeOptionalLanguageCode(string? languageCode)
    {
        string canonicalCode = LanguageCodeUtility.Canonicalize(languageCode);
        return string.IsNullOrWhiteSpace(canonicalCode) ? null : canonicalCode;
    }
}
