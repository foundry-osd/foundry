using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Autopilot;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Generates the reduced Foundry.Deploy runtime configuration from Foundry configuration settings.
/// </summary>
public sealed class DeployConfigurationGenerator : IDeployConfigurationGenerator
{
    /// <inheritdoc />
    public FoundryDeployConfigurationDocument Generate(FoundryConfigurationDocument document)
    {
        return Generate(document, mediaSecretsKey: null);
    }

    /// <summary>
    /// Generates the reduced Foundry.Deploy runtime configuration and embeds encrypted media-only secrets when required.
    /// </summary>
    /// <param name="document">User-facing Foundry configuration.</param>
    /// <param name="mediaSecretsKey">Media secret key used to encrypt boot-media-only secrets.</param>
    /// <returns>Reduced Foundry.Deploy configuration document.</returns>
    public FoundryDeployConfigurationDocument Generate(FoundryConfigurationDocument document, byte[]? mediaSecretsKey)
    {
        ArgumentNullException.ThrowIfNull(document);
        AutopilotConfigurationValidator.ThrowIfNotReady(document.Autopilot, DateTimeOffset.UtcNow);

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
                },
                Oobe = MapOobeSettings(document.Customization.Oobe),
                AppxRemoval = MapAppxRemovalSettings(document.Customization.AppxRemoval),
                AiComponentRemoval = MapAiComponentRemovalSettings(
                    document.Customization.AiComponentRemoval,
                    document.Customization.AppxRemoval)
            },
            Autopilot = new DeployAutopilotSettings
            {
                IsEnabled = document.Autopilot.IsEnabled,
                ProvisioningMode = document.Autopilot.ProvisioningMode,
                DefaultProfileFolderName = document.Autopilot.ProvisioningMode == AutopilotProvisioningMode.JsonProfile
                    ? document.Autopilot.Profiles
                        .FirstOrDefault(profile => string.Equals(profile.Id, document.Autopilot.DefaultProfileId, StringComparison.OrdinalIgnoreCase))
                        ?.FolderName
                    : null,
                HardwareHashUpload = CreateDeployHardwareHashUploadSettings(
                    document.Autopilot,
                    mediaSecretsKey)
            },
            Telemetry = document.Telemetry
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

    private static DeployAutopilotHardwareHashUploadSettings CreateDeployHardwareHashUploadSettings(
        AutopilotSettings autopilot,
        byte[]? mediaSecretsKey)
    {
        AutopilotHardwareHashUploadSettings? settings = autopilot.HardwareHashUpload;
        if (settings?.Tenant is null)
        {
            return new DeployAutopilotHardwareHashUploadSettings();
        }

        SecretEnvelope? pfxSecret = null;
        SecretEnvelope? pfxPasswordSecret = null;
        if (autopilot.IsEnabled &&
            autopilot.ProvisioningMode == AutopilotProvisioningMode.HardwareHashUpload &&
            mediaSecretsKey is not null)
        {
            if (mediaSecretsKey.Length == 0)
            {
                throw new InvalidOperationException("Autopilot hardware hash upload media generation requires a media secret key.");
            }

            AutopilotBootMediaCertificateSettings bootMediaCertificate = settings.BootMediaCertificate;
            if (string.IsNullOrWhiteSpace(bootMediaCertificate.PfxPath) ||
                !File.Exists(bootMediaCertificate.PfxPath))
            {
                throw new InvalidOperationException("Autopilot hardware hash upload media generation requires the selected PFX file.");
            }

            if (string.IsNullOrWhiteSpace(bootMediaCertificate.PfxPassword))
            {
                throw new InvalidOperationException("Autopilot hardware hash upload media generation requires the selected PFX password.");
            }

            byte[] pfxBytes = File.ReadAllBytes(bootMediaCertificate.PfxPath);
            try
            {
                pfxSecret = MediaSecretEnvelopeProtector.EncryptBytes(pfxBytes, mediaSecretsKey);
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(pfxBytes);
            }

            pfxPasswordSecret = MediaSecretEnvelopeProtector.EncryptString(
                bootMediaCertificate.PfxPassword,
                mediaSecretsKey);
        }

        return new DeployAutopilotHardwareHashUploadSettings
        {
            TenantId = settings.Tenant.TenantId,
            ClientId = settings.Tenant.ClientId,
            ActiveCertificateKeyId = settings.ActiveCertificate?.KeyId,
            ActiveCertificateThumbprint = settings.ActiveCertificate?.Thumbprint,
            ActiveCertificateExpiresOnUtc = settings.ActiveCertificate?.ExpiresOnUtc,
            DefaultGroupTag = settings.DefaultGroupTag,
            KnownGroupTags = CanonicalizeGroupTags(settings.KnownGroupTags),
            CertificatePfxSecret = pfxSecret,
            CertificatePfxPasswordSecret = pfxPasswordSecret
        };
    }

    private static string[] CanonicalizeGroupTags(IEnumerable<string> groupTags)
    {
        ArgumentNullException.ThrowIfNull(groupTags);

        return groupTags
            .Select(groupTag => groupTag.Trim())
            .Where(groupTag => !string.IsNullOrWhiteSpace(groupTag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DeployOobeSettings MapOobeSettings(OobeSettings settings)
    {
        if (!settings.IsEnabled)
        {
            return new DeployOobeSettings();
        }

        return new DeployOobeSettings
        {
            IsEnabled = true,
            SkipLicenseTerms = settings.SkipLicenseTerms,
            DiagnosticDataLevel = MapDiagnosticDataLevel(settings.DiagnosticDataLevel),
            HidePrivacySetup = settings.HidePrivacySetup,
            AllowTailoredExperiences = settings.AllowTailoredExperiences,
            AllowAdvertisingId = settings.AllowAdvertisingId,
            AllowOnlineSpeechRecognition = settings.AllowOnlineSpeechRecognition,
            AllowInkingAndTypingDiagnostics = settings.AllowInkingAndTypingDiagnostics,
            LocationAccess = MapLocationAccess(settings.LocationAccess)
        };
    }

    private static DeployOobeDiagnosticDataLevel MapDiagnosticDataLevel(OobeDiagnosticDataLevel value)
    {
        return value switch
        {
            OobeDiagnosticDataLevel.Optional => DeployOobeDiagnosticDataLevel.Optional,
            OobeDiagnosticDataLevel.Off => DeployOobeDiagnosticDataLevel.Off,
            _ => DeployOobeDiagnosticDataLevel.Required
        };
    }

    private static DeployOobeLocationAccessMode MapLocationAccess(OobeLocationAccessMode value)
    {
        return value == OobeLocationAccessMode.ForceOff
            ? DeployOobeLocationAccessMode.ForceOff
            : DeployOobeLocationAccessMode.UserControlled;
    }

    private static DeployAppxRemovalSettings MapAppxRemovalSettings(AppxRemovalSettings settings)
    {
        string[] packageNames = CanonicalizePackageNames(settings.PackageNames);
        return settings.IsEnabled && packageNames.Length > 0
            ? new DeployAppxRemovalSettings
            {
                IsEnabled = true,
                PackageNames = packageNames
            }
            : new DeployAppxRemovalSettings();
    }

    private static DeployAiComponentRemovalSettings MapAiComponentRemovalSettings(
        AiComponentRemovalSettings settings,
        AppxRemovalSettings legacyAppxRemoval)
    {
        bool removeCopilot = settings.IsEnabled && settings.RemoveCopilot ||
            HasLegacyAppxRemovalPackage(legacyAppxRemoval, "Microsoft.Copilot");
        bool removeAiHub = settings.IsEnabled && settings.RemoveAiHub ||
            HasLegacyAppxRemovalPackage(legacyAppxRemoval, "Microsoft.Windows.AIHub");
        bool isEnabled = settings.IsEnabled || removeCopilot || removeAiHub;
        var effectiveSettings = new AiComponentRemovalSettings
        {
            IsEnabled = isEnabled,
            RemoveCopilot = removeCopilot,
            RemoveAiHub = removeAiHub,
            DisableRecall = settings.IsEnabled && settings.DisableRecall,
            DisableClickToDo = settings.IsEnabled && settings.DisableClickToDo,
            DisableAiServiceAutoStart = settings.IsEnabled && settings.DisableAiServiceAutoStart,
            DisableEdgeAi = settings.IsEnabled && settings.DisableEdgeAi,
            DisablePaintAi = settings.IsEnabled && settings.DisablePaintAi,
            DisableNotepadAi = settings.IsEnabled && settings.DisableNotepadAi
        };

        if (!effectiveSettings.IsEnabled || !HasAnyAiComponentRemovalOptionEnabled(effectiveSettings))
        {
            return new DeployAiComponentRemovalSettings();
        }

        return new DeployAiComponentRemovalSettings
        {
            IsEnabled = true,
            RemoveCopilot = effectiveSettings.RemoveCopilot,
            RemoveAiHub = effectiveSettings.RemoveAiHub,
            DisableRecall = effectiveSettings.DisableRecall,
            DisableClickToDo = effectiveSettings.DisableClickToDo,
            DisableAiServiceAutoStart = effectiveSettings.DisableAiServiceAutoStart,
            DisableEdgeAi = effectiveSettings.DisableEdgeAi,
            DisablePaintAi = effectiveSettings.DisablePaintAi,
            DisableNotepadAi = effectiveSettings.DisableNotepadAi
        };
    }

    private static bool HasAnyAiComponentRemovalOptionEnabled(AiComponentRemovalSettings settings)
    {
        return settings.RemoveCopilot ||
            settings.RemoveAiHub ||
            settings.DisableRecall ||
            settings.DisableClickToDo ||
            settings.DisableAiServiceAutoStart ||
            settings.DisableEdgeAi ||
            settings.DisablePaintAi ||
            settings.DisableNotepadAi;
    }

    private static bool HasLegacyAppxRemovalPackage(AppxRemovalSettings settings, string packageName)
    {
        return settings.IsEnabled &&
            settings.PackageNames.Any(value => string.Equals(value.Trim(), packageName, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] CanonicalizePackageNames(IEnumerable<string> packageNames)
    {
        ArgumentNullException.ThrowIfNull(packageNames);

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<string> result = [];
        foreach (string packageName in packageNames)
        {
            string trimmed = packageName.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) ||
                !AppxRemovalCatalog.ContainsPackageName(trimmed) ||
                !seen.Add(trimmed))
            {
                continue;
            }

            result.Add(trimmed);
        }

        return result.ToArray();
    }
}
