using System.Globalization;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Media;
using Foundry.Core.Services.WinPe;
using Foundry.Telemetry;

namespace Foundry.Core.Services.Telemetry;

/// <summary>
/// Builds the event-specific properties for completed Foundry OSD boot media creation telemetry.
/// </summary>
public static class BootMediaTelemetryPropertyBuilder
{
    /// <summary>
    /// Creates the low-cardinality `osd:boot_media_finished` property set without sensitive configuration values.
    /// </summary>
    /// <param name="bootMediaTarget">Final media target value.</param>
    /// <param name="options">Resolved media creation options.</param>
    /// <param name="document">Current expert configuration document.</param>
    /// <param name="success">Whether media creation completed successfully.</param>
    /// <param name="failedStepName">Failed media creation step name, or <see langword="null"/> when successful.</param>
    /// <param name="duration">Total media creation duration.</param>
    /// <param name="connectRuntimePayloadSource">Source of the generated Connect runtime payload.</param>
    /// <param name="deployRuntimePayloadSource">Source of the generated Deploy runtime payload.</param>
    /// <returns>Telemetry properties approved for `osd:boot_media_finished`.</returns>
    public static IReadOnlyDictionary<string, object?> Build(
        string bootMediaTarget,
        MediaPreflightOptions options,
        FoundryExpertConfigurationDocument document,
        bool success,
        string? failedStepName,
        TimeSpan duration,
        string connectRuntimePayloadSource,
        string deployRuntimePayloadSource)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(document);

        var properties = new Dictionary<string, object?>
        {
            ["boot_media_target"] = bootMediaTarget,
            ["boot_media_creation_success"] = success,
            ["boot_media_creation_duration_seconds"] = Math.Round(duration.TotalSeconds, 2),
            ["boot_media_creation_failed_step_name"] = failedStepName,
            ["boot_media_architecture"] = options.Architecture.ToString().ToLowerInvariant(),
            ["boot_media_winpe_language"] = NormalizeCultureName(options.WinPeLanguage).ToLowerInvariant(),
            ["boot_media_boot_image_source"] = options.BootImageSource.ToString().ToLowerInvariant(),
            ["boot_media_signature_mode"] = options.SignatureMode.ToString().ToLowerInvariant(),
            ["boot_media_usb_partition_style"] = bootMediaTarget == TelemetryBootMediaTargets.Usb
                ? options.UsbPartitionStyle.ToString().ToLowerInvariant()
                : "none",
            ["boot_media_usb_format_mode"] = bootMediaTarget == TelemetryBootMediaTargets.Usb
                ? options.UsbFormatMode.ToString().ToLowerInvariant()
                : "none",
            ["boot_media_drivers_dell_enabled"] = options.DriverVendors.Contains(WinPeVendorSelection.Dell),
            ["boot_media_drivers_hp_enabled"] = options.DriverVendors.Contains(WinPeVendorSelection.Hp),
            ["boot_media_drivers_custom_enabled"] = !string.IsNullOrWhiteSpace(options.CustomDriverDirectoryPath),
            ["boot_media_connect_runtime_payload_source"] = connectRuntimePayloadSource,
            ["boot_media_deploy_runtime_payload_source"] = deployRuntimePayloadSource,
            ["autopilot_enabled"] = options.IsAutopilotEnabled
        };

        AddCustomizationTelemetryProperties(properties, document.Customization);
        AddLocalizationTelemetryProperties(properties, document.Localization);
        AddNetworkTelemetryProperties(properties, document.Network, options.AreRequiredSecretsReady);

        return properties;
    }

    private static void AddCustomizationTelemetryProperties(
        IDictionary<string, object?> properties,
        CustomizationSettings customization)
    {
        MachineNamingSettings machineNaming = customization.MachineNaming;
        OobeSettings oobe = customization.Oobe;
        AppxRemovalSettings appxRemoval = customization.AppxRemoval;
        string[] selectedAppxPackages = ResolveSelectedAppxPackages(appxRemoval);
        bool isAppxRemovalEnabled = appxRemoval.IsEnabled && selectedAppxPackages.Length > 0;

        properties["customization_any_enabled"] = machineNaming.IsEnabled || oobe.IsEnabled || isAppxRemovalEnabled;
        properties["customization_machine_naming_enabled"] = machineNaming.IsEnabled;
        properties["customization_machine_naming_mode"] = ResolveMachineNamingTelemetryMode(machineNaming);
        properties["customization_machine_naming_prefix_configured"] =
            machineNaming.IsEnabled && !string.IsNullOrWhiteSpace(machineNaming.Prefix);
        properties["customization_oobe_enabled"] = oobe.IsEnabled;
        properties["customization_oobe_skip_license_terms"] = oobe.IsEnabled && oobe.SkipLicenseTerms;
        properties["customization_oobe_diagnostic_data_level"] = ToTelemetryValue(oobe.DiagnosticDataLevel);
        properties["customization_oobe_hide_privacy_setup"] = oobe.IsEnabled && oobe.HidePrivacySetup;
        properties["customization_oobe_tailored_experiences_enabled"] = oobe.IsEnabled && oobe.AllowTailoredExperiences;
        properties["customization_oobe_advertising_id_enabled"] = oobe.IsEnabled && oobe.AllowAdvertisingId;
        properties["customization_oobe_online_speech_recognition_enabled"] = oobe.IsEnabled && oobe.AllowOnlineSpeechRecognition;
        properties["customization_oobe_inking_typing_diagnostics_enabled"] = oobe.IsEnabled && oobe.AllowInkingAndTypingDiagnostics;
        properties["customization_oobe_location_access"] = ToTelemetryValue(oobe.LocationAccess);
        properties["customization_appx_removal_enabled"] = isAppxRemovalEnabled;
        properties["customization_appx_removal_package_count"] = isAppxRemovalEnabled ? selectedAppxPackages.Length : 0;
        properties["customization_appx_removal_profile"] = ResolveAppxRemovalProfile(appxRemoval, selectedAppxPackages, isAppxRemovalEnabled);
    }

    private static void AddLocalizationTelemetryProperties(
        IDictionary<string, object?> properties,
        LocalizationSettings localization)
    {
        int visibleLanguagesCount = localization.VisibleLanguageCodes.Count;
        bool hasDefaultLanguage = !string.IsNullOrWhiteSpace(localization.DefaultLanguageCodeOverride);
        bool hasTimeZone = !string.IsNullOrWhiteSpace(localization.DefaultTimeZoneId);

        properties["localization_any_enabled"] = visibleLanguagesCount > 0 || hasDefaultLanguage || hasTimeZone;
        properties["localization_visible_languages_count"] = visibleLanguagesCount;
        properties["localization_default_language_configured"] = hasDefaultLanguage;
        properties["localization_time_zone_configured"] = hasTimeZone;
    }

    private static void AddNetworkTelemetryProperties(
        IDictionary<string, object?> properties,
        NetworkSettings network,
        bool areRequiredSecretsReady)
    {
        Dot1xSettings dot1x = network.Dot1x;
        WifiSettings wifi = network.Wifi;
        bool isDot1xProfileConfigured = dot1x.IsEnabled && !string.IsNullOrWhiteSpace(dot1x.ProfileTemplatePath);
        bool isDot1xCertificateConfigured = dot1x.IsEnabled && !string.IsNullOrWhiteSpace(dot1x.CertificatePath);
        bool isWifiEnterpriseProfileConfigured = wifi.IsEnabled && wifi.HasEnterpriseProfile && !string.IsNullOrWhiteSpace(wifi.EnterpriseProfileTemplatePath);
        bool isWifiEnterpriseCertificateConfigured = wifi.IsEnabled && !string.IsNullOrWhiteSpace(wifi.CertificatePath);

        properties["network_any_enabled"] = dot1x.IsEnabled || network.WifiProvisioned || wifi.IsEnabled;
        properties["network_wired_dot1x_enabled"] = dot1x.IsEnabled;
        properties["network_wired_dot1x_profile_configured"] = isDot1xProfileConfigured;
        properties["network_wired_dot1x_certificate_required"] = dot1x.IsEnabled && dot1x.RequiresCertificate;
        properties["network_wired_dot1x_certificate_configured"] = isDot1xCertificateConfigured;
        properties["network_wifi_provisioning_enabled"] = network.WifiProvisioned;
        properties["network_wifi_profile_configured"] = wifi.IsEnabled &&
            (!string.IsNullOrWhiteSpace(wifi.Ssid) || isWifiEnterpriseProfileConfigured);
        properties["network_wifi_security_type"] = ResolveNetworkWifiSecurityTelemetryValue(wifi);
        properties["network_wifi_ssid_configured"] = wifi.IsEnabled && !string.IsNullOrWhiteSpace(wifi.Ssid);
        properties["network_wifi_passphrase_configured"] = RequiresPersonalWifiPassphrase(wifi) && areRequiredSecretsReady && network.WifiProvisioned;
        properties["network_wifi_enterprise_profile_configured"] = isWifiEnterpriseProfileConfigured;
        properties["network_wifi_enterprise_certificate_required"] = wifi.IsEnabled && wifi.RequiresCertificate;
        properties["network_wifi_enterprise_certificate_configured"] = isWifiEnterpriseCertificateConfigured;
    }

    private static string ResolveMachineNamingTelemetryMode(MachineNamingSettings settings)
    {
        if (!settings.IsEnabled)
        {
            return "disabled";
        }

        if (!settings.AutoGenerateName)
        {
            return "manual";
        }

        return settings.AllowManualSuffixEdit
            ? "auto_generated_editable"
            : "auto_generated_locked";
    }

    private static string[] ResolveSelectedAppxPackages(AppxRemovalSettings appxRemoval)
    {
        if (!appxRemoval.IsEnabled)
        {
            return [];
        }

        return appxRemoval.PackageNames
            .Where(packageName => !string.IsNullOrWhiteSpace(packageName))
            .Select(packageName => packageName.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(packageName => packageName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveAppxRemovalProfile(AppxRemovalSettings settings, string[] selectedPackageNames, bool isEnabled)
    {
        if (!isEnabled)
        {
            return "none";
        }

        var selectedPackages = selectedPackageNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] selectedProfileNames = settings.ProfileNames is null
            ? InferAppxRemovalProfileNames(selectedPackages).ToArray()
            : settings.ProfileNames
                .Where(profileName => !string.IsNullOrWhiteSpace(profileName))
                .Select(profileName => profileName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (selectedProfileNames.Length == 0)
        {
            return "custom";
        }

        var selectedCategoryTokens = new List<string>(selectedProfileNames.Length);
        HashSet<string> expectedPackages = new(StringComparer.OrdinalIgnoreCase);
        foreach (string profileName in selectedProfileNames)
        {
            string[] profilePackages = AppxRemovalCatalog.Entries
                .Where(entry => string.Equals(entry.Category, profileName, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.PackageName)
                .ToArray();
            if (profilePackages.Length == 0)
            {
                return "custom";
            }

            expectedPackages.UnionWith(profilePackages);
            selectedCategoryTokens.Add(ToTelemetryToken(profileName));
        }

        if (!selectedPackages.SetEquals(expectedPackages))
        {
            return "custom";
        }

        if (selectedCategoryTokens.Count == 1)
        {
            return selectedCategoryTokens[0];
        }

        if (selectedCategoryTokens.Count > 1)
        {
            return "multiple";
        }

        return "custom";
    }

    private static IEnumerable<string> InferAppxRemovalProfileNames(HashSet<string> selectedPackages)
    {
        int matchedPackageCount = 0;
        var profileNames = new List<string>();
        foreach (IGrouping<string, AppxRemovalCatalogEntry> category in AppxRemovalCatalog.Entries.GroupBy(entry => entry.Category))
        {
            string[] categoryPackages = category
                .Select(entry => entry.PackageName)
                .ToArray();
            int selectedCategoryPackageCount = categoryPackages.Count(selectedPackages.Contains);
            if (selectedCategoryPackageCount == 0)
            {
                continue;
            }

            if (selectedCategoryPackageCount != categoryPackages.Length)
            {
                yield break;
            }

            matchedPackageCount += selectedCategoryPackageCount;
            profileNames.Add(category.Key);
        }

        if (matchedPackageCount != selectedPackages.Count)
        {
            yield break;
        }

        foreach (string profileName in profileNames)
        {
            yield return profileName;
        }
    }

    private static string ToTelemetryToken(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        bool previousWasSeparator = false;

        foreach (char ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator && builder.Length > 0)
            {
                builder.Append('_');
                previousWasSeparator = true;
            }
        }

        return builder.ToString().Trim('_');
    }

    private static string ToTelemetryValue(OobeDiagnosticDataLevel value)
    {
        return value switch
        {
            OobeDiagnosticDataLevel.Optional => "optional",
            OobeDiagnosticDataLevel.Off => "off",
            _ => "required"
        };
    }

    private static string ToTelemetryValue(OobeLocationAccessMode value)
    {
        return value switch
        {
            OobeLocationAccessMode.ForceOff => "force_off",
            _ => "user_controlled"
        };
    }

    private static string ResolveNetworkWifiSecurityTelemetryValue(WifiSettings wifi)
    {
        if (!wifi.IsEnabled)
        {
            return "none";
        }

        string normalizedSecurity = NetworkConfigurationValidator.NormalizeWifiSecurityType(wifi);
        return normalizedSecurity switch
        {
            NetworkConfigurationValidator.WifiSecurityOpen => "open",
            NetworkConfigurationValidator.WifiSecurityOwe => "owe",
            NetworkConfigurationValidator.WifiSecurityPersonal => "personal",
            NetworkConfigurationValidator.WifiSecurityEnterprise => "enterprise",
            NetworkConfigurationValidator.WifiSecurityEnterpriseWpa3 => "enterprise",
            NetworkConfigurationValidator.WifiSecurityEnterpriseWpa3192 => "enterprise",
            _ => "unknown"
        };
    }

    private static bool RequiresPersonalWifiPassphrase(WifiSettings wifi)
    {
        return wifi.IsEnabled &&
            !wifi.HasEnterpriseProfile &&
            string.Equals(
                NetworkConfigurationValidator.NormalizeWifiSecurityType(wifi),
                NetworkConfigurationValidator.WifiSecurityPersonal,
                StringComparison.Ordinal);
    }

    private static string NormalizeCultureName(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
        {
            return "unknown";
        }

        try
        {
            return CultureInfo.GetCultureInfo(cultureName.Trim()).Name;
        }
        catch (CultureNotFoundException)
        {
            return cultureName.Trim();
        }
    }
}
