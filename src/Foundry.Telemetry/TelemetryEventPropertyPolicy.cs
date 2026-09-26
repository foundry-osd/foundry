// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;

namespace Foundry.Telemetry;

/// <summary>
/// Applies explicit per-event property allowlists before telemetry leaves the process.
/// </summary>
public static class TelemetryEventPropertyPolicy
{
    private static readonly HashSet<string> KnownLanguageNames = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
        .Where(culture => (culture.CultureTypes & CultureTypes.UserCustomCulture) == 0)
        .Select(culture => culture.Name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "ssid",
        "bssid",
        "password",
        "passphrase",
        "secret",
        "token",
        "path",
        "file_path",
        "iso_output_path",
        "custom_driver_path",
        "disk_name",
        "disk_serial",
        "disk_number",
        "computer_name",
        "target_computer_name",
        "autopilot_profile_name",
        "autopilot_profile_folder",
        "tenant_id",
        "client_id",
        "certificate_thumbprint",
        "certificate_key_id",
        "group_tag",
        "serial_number",
        "hardware_hash",
        "autopilot_device_id",
        "import_id",
        "username",
        "domain",
        "email",
        "ip_address"
    };

    private static readonly IReadOnlyDictionary<string, HashSet<string>> AllowedPropertiesByEvent =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            [TelemetryEvents.BootstrapFailed] = new(StringComparer.Ordinal)
            {
                "failure_category", "last_stage", "elapsed_seconds", "child_application",
                "child_exit_code", "payload_source", "payload_version", "architecture", "child_startup_stage"
            },
            [TelemetryEvents.AppDailyActive] = new(StringComparer.Ordinal)
            {
                "proxy_method",
                "proxy_authentication_mode"
            },
            [TelemetryEvents.OsdBootMediaFinished] = new(StringComparer.Ordinal)
            {
                "boot_media_target",
                "boot_media_usb_operation",
                "boot_media_creation_success",
                "boot_media_creation_duration_seconds",
                "boot_media_creation_failed_step_name",
                "boot_media_failure_kind",
                "boot_media_failure_reason",
                "boot_media_failure_code",
                "boot_media_failure_tool",
                "boot_media_failure_exit_code",
                "operation_id",
                "boot_media_architecture",
                "boot_media_winpe_language",
                "boot_media_boot_image_source",
                "boot_media_signature_mode",
                "boot_media_usb_partition_style",
                "boot_media_usb_format_mode",
                "boot_media_drivers_dell_enabled",
                "boot_media_drivers_hp_enabled",
                "boot_media_drivers_custom_enabled",
                "boot_media_custom_images_enabled",
                "boot_media_custom_images_count",
                "boot_media_default_os_source",
                "boot_media_connect_runtime_payload_source",
                "boot_media_deploy_runtime_payload_source",
                "autopilot_enabled",
                "autopilot_provisioning_mode",
                "deployment_protection_enabled",
                "unattend_enabled",
                "unattend_default_mode",
                "unattend_file_count",
                "deployment_reboot_mode",
                "deployment_reboot_delay_seconds",
                "customization_any_enabled",
                "customization_machine_naming_enabled",
                "customization_machine_naming_mode",
                "customization_machine_naming_component_count",
                "customization_machine_naming_component_types",
                "customization_machine_naming_separator",
                "customization_machine_naming_casing",
                "customization_machine_naming_truncation_directions",
                "customization_machine_naming_editing_enabled",
                "customization_oobe_enabled",
                "customization_oobe_skip_license_terms",
                "customization_oobe_diagnostic_data_level",
                "customization_oobe_hide_privacy_setup",
                "customization_oobe_tailored_experiences_enabled",
                "customization_oobe_advertising_id_enabled",
                "customization_oobe_online_speech_recognition_enabled",
                "customization_oobe_inking_typing_diagnostics_enabled",
                "customization_oobe_location_access",
                "customization_appx_removal_enabled",
                "customization_appx_removal_package_count",
                "customization_appx_removal_profile",
                "customization_windows_optional_features_enabled",
                "customization_windows_optional_features_configured_count",
                "customization_windows_optional_features_enable_count",
                "customization_windows_optional_features_disable_count",
                "customization_windows_optional_features_category_count",
                "customization_windows_optional_features_requires_sxs",
                "customization_ai_component_removal_enabled",
                "customization_ai_remove_copilot_enabled",
                "customization_ai_remove_ai_hub_enabled",
                "customization_ai_disable_recall_enabled",
                "customization_ai_disable_click_to_do_enabled",
                "customization_ai_disable_service_autostart_enabled",
                "customization_ai_disable_edge_ai_enabled",
                "customization_ai_disable_paint_ai_enabled",
                "customization_ai_disable_notepad_ai_enabled",
                "customization_ai_component_removal_option_count",
                "os_selection_enabled",
                "os_selection_any_configured",
                "os_selection_allowed_languages_count",
                "os_selection_default_language_configured",
                "os_selection_allowed_release_count",
                "os_selection_default_release_configured",
                "os_selection_default_update_offset",
                "os_selection_allowed_license_channel_count",
                "os_selection_default_license_channel_configured",
                "os_selection_allowed_edition_count",
                "os_selection_default_edition_configured",
                "deployment_time_zone_configured",
                "network_any_enabled",
                "network_wired_dot1x_enabled",
                "network_wired_dot1x_profile_configured",
                "network_wired_dot1x_certificate_required",
                "network_wired_dot1x_certificate_configured",
                "network_wifi_provisioning_enabled",
                "network_wifi_profile_configured",
                "network_wifi_security_type",
                "network_wifi_ssid_configured",
                "network_wifi_passphrase_configured",
                "network_wifi_enterprise_profile_configured",
                "network_wifi_enterprise_certificate_required",
                "network_wifi_enterprise_certificate_configured",
                "network_profile_roaming_enabled",
                "network_private_key_roaming_enabled",
                "network_wired_dot1x_profile_roaming_enabled",
                "network_wired_dot1x_private_key_roaming_enabled",
                "network_wifi_profile_roaming_enabled",
                "network_wifi_private_key_roaming_enabled"
            },
            [TelemetryEvents.ConnectSessionReady] = new(StringComparer.Ordinal)
            {
                "connect_network_connection_type",
                "connect_network_layout_mode",
                "connect_ethernet_available",
                "connect_wifi_available",
                "connect_wifi_security_type",
                "connect_wifi_source",
                "connect_wired_dot1x_enabled",
                "connect_wifi_provisioned"
            },
            [TelemetryEvents.DeploySessionFinished] = new(StringComparer.Ordinal)
            {
                "operation_id",
                "deploy_session_success",
                "deploy_session_cancelled",
                "deploy_session_duration_seconds",
                "deploy_session_completed_step_count",
                "deploy_session_failed_step_name",
                "deploy_session_failed_operation_name",
                "deploy_session_failure_kind",
                "deploy_session_failure_code",
                "deploy_session_failure_reason",
                "deploy_session_mode",
                "deploy_unattend_mode",
                "deploy_session_dry_run_enabled",
                "deploy_hardware_vendor",
                "deploy_hardware_model",
                "deploy_hardware_virtual_machine",
                "deploy_os_product",
                "deploy_os_source",
                "deploy_os_version",
                "deploy_os_build",
                "deploy_os_update_month",
                "deploy_os_architecture",
                "deploy_os_language",
                "deploy_os_edition",
                "deploy_os_license_channel",
                "deploy_os_image_index",
                "deploy_driver_pack_selection_kind",
                "deploy_driver_pack_vendor",
                "deploy_driver_pack_model",
                "deploy_firmware_updates_enabled",
                "deploy_autopilot_enabled",
                "deploy_autopilot_provisioning_mode",
                "deploy_autopilot_hash_upload_state",
                "deploy_autopilot_hash_group_tag_selected",
                "deploy_completion_reboot_mode",
                "deploy_completion_reboot_delay_seconds"
            }
        };

    /// <summary>
    /// Returns whether the event name is part of the approved telemetry taxonomy.
    /// </summary>
    /// <param name="eventName">Telemetry event name to validate.</param>
    /// <returns><see langword="true"/> when the event is allowed to leave the process.</returns>
    public static bool IsKnownEvent(string eventName)
    {
        return AllowedPropertiesByEvent.ContainsKey(eventName);
    }

    /// <summary>
    /// Returns only approved properties, limiting custom-image metadata to recognized technical values.
    /// </summary>
    /// <param name="eventName">Stable telemetry event name.</param>
    /// <param name="properties">Candidate event properties before filtering.</param>
    /// <returns>Allowed event properties with known sensitive values removed.</returns>
    public static IReadOnlyDictionary<string, object?> Sanitize(string eventName, IReadOnlyDictionary<string, object?> properties)
    {
        if (!AllowedPropertiesByEvent.TryGetValue(eventName, out HashSet<string>? allowedProperties))
        {
            return new Dictionary<string, object?>();
        }

        bool isCustomDeployment = eventName == TelemetryEvents.DeploySessionFinished &&
            properties.TryGetValue("deploy_os_source", out object? source) && source is "custom";
        Dictionary<string, object?> sanitized = new(StringComparer.Ordinal);
        foreach ((string key, object? value) in properties)
        {
            if (!allowedProperties.Contains(key) || IsSensitiveKey(key) || !IsAllowedUnattendValue(key, value) ||
                !IsAllowedCustomImageValue(key, value))
            {
                continue;
            }

            if (eventName == TelemetryEvents.BootstrapFailed)
            {
                if (key == "elapsed_seconds")
                {
                    if (value is double seconds && double.IsFinite(seconds) && seconds >= 0)
                    {
                        sanitized[key] = Math.Min(seconds, 604800);
                    }
                }
                else if (key == "child_exit_code")
                {
                    if (value is int) sanitized[key] = value;
                }
                else if (value is string text && IsAllowedBootstrapValue(key, text))
                {
                    sanitized[key] = text;
                }
                continue;
            }

            sanitized[key] = isCustomDeployment
                ? SanitizeCustomImageMetadata(key, value)
                : value;
        }

        return sanitized;
    }

    private static bool IsAllowedBootstrapValue(string key, string value) => key switch
    {
        "failure_category" => value is "child_exit" or "stage_failed" or "readiness_timeout" or "startup_failed" or "capability_invalid",
        "last_stage" => value is "environment" or "connect" or "system" or "deploymentpreparation" or "deploy",
        "child_application" => value is TelemetryApps.FoundryConnect or TelemetryApps.FoundryDeploy or "none",
        "payload_source" => value is TelemetryRuntimePayloadSources.None or TelemetryRuntimePayloadSources.Debug or
            TelemetryRuntimePayloadSources.Release or TelemetryRuntimePayloadSources.Unknown,
        "architecture" => value is "x64" or "arm64",
        "child_startup_stage" => value is "managed_started" or "configuration_loaded" or "ui_ready" or "startup_failed",
        "payload_version" => value.Length <= 64 && Version.TryParse(value, out _),
        _ => false
    };

    private static bool IsAllowedUnattendValue(string key, object? value) => key switch
    {
        "unattend_enabled" => value is bool,
        "unattend_default_mode" or "deploy_unattend_mode" => value is "native" or "custom",
        "unattend_file_count" => value is int and >= 0 and <= 100,
        _ => true
    };

    private static bool IsAllowedCustomImageValue(string key, object? value) => key switch
    {
        "boot_media_custom_images_enabled" => value is bool,
        "boot_media_custom_images_count" => value is int and >= 0 and <= 256,
        "boot_media_default_os_source" or "deploy_os_source" => value is "catalog" or "custom",
        _ => true
    };

    private static object? SanitizeCustomImageMetadata(string key, object? value)
    {
        string text = value is string metadata ? metadata.ToLowerInvariant() : string.Empty;
        return key switch
        {
            "deploy_os_product" => text is "windows" or "windows_10" or "windows_11" ? text : "unknown",
            "deploy_os_version" => text.Length == 4 && char.IsAsciiDigit(text[0]) && char.IsAsciiDigit(text[1]) &&
                text[2] == 'h' && text[3] is '1' or '2' ? text : "unknown",
            "deploy_os_build" => text.Length <= 64 && text.All(character => char.IsAsciiDigit(character) || character == '.') &&
                Version.TryParse(text, out _) ? text : "unknown",
            "deploy_os_architecture" => text is "x86" or "x64" or "arm" or "arm64" ? text : "unknown",
            "deploy_os_language" => KnownLanguageNames.Contains(text) ? text : "unknown",
            "deploy_os_edition" => IsKnownWindowsEdition(text) ? text : "unknown",
            "deploy_os_license_channel" or "deploy_os_update_month" => "unknown",
            _ => value
        };
    }

    private static bool IsKnownWindowsEdition(string value) => value is
        "core" or "coren" or "corecountryspecific" or "coresinglelanguage" or
        "professional" or "professionaln" or "professionaleducation" or "professionaleducationn" or
        "professionalworkstation" or "professionalworkstationn" or
        "education" or "educationn" or "enterprise" or "enterprisen" or "enterprises" or "enterprisesn" or
        "enterpriseeval" or "enterpriseneval" or "enterpriseseval" or "enterprisesneval" or
        "iotenterprise" or "iotenterprises" or "iotenterprisek" or "iotenterprisesk" or
        "cloud" or "cloudn" or "cloudedition" or "cloudeditionn" or "ppipro" or
        "starter" or "startern" or "homebasic" or "homebasicn" or "homepremium" or "homepremiumn" or
        "ultimate" or "ultimaten" or "business" or "businessn" or "embedded" or "embeddedindustry" or
        "embeddedindustrye" or "embeddedindustrya" or
        "serverstandard" or "serverstandardcore" or "serverstandardeval" or "serverstandardevalcore" or
        "serverdatacenter" or "serverdatacentercore" or "serverdatacentereval" or "serverdatacenterevalcore" or
        "serverdatacenterazureedition" or "serverdatacenterazureeditioncore" or
        "serversolution" or "serversolutioncore" or "serverenterprise" or "serverenterprisecore" or
        "serverweb" or "serverwebcore" or "serverhyper" or "azurestackhci";

    private static bool IsSensitiveKey(string key)
    {
        return SensitiveKeys.Contains(key) ||
            key.Contains("path", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("url", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("exception", StringComparison.OrdinalIgnoreCase);
    }
}
