using Foundry.Telemetry;

namespace Foundry.Telemetry.Tests;

public sealed class TelemetryEventPropertyPolicyTests
{
    [Fact]
    public void Sanitize_ForBootMediaFinished_DropsPropertiesOutsideEventAllowlist()
    {
        Dictionary<string, object?> input = new()
        {
            ["boot_media_target"] = "iso",
            ["boot_media_creation_success"] = true,
            ["boot_media_creation_duration_seconds"] = 12.5,
            ["boot_media_architecture"] = "x64",
            ["boot_media_creation_failed_step_name"] = "Prepare WinPE workspace",
            ["boot_media_winpe_language"] = "en-us",
            ["boot_media_boot_image_source"] = "winpe_adk",
            ["boot_media_signature_mode"] = "signed",
            ["boot_media_usb_partition_style"] = "gpt",
            ["boot_media_usb_format_mode"] = "quick",
            ["boot_media_drivers_dell_enabled"] = true,
            ["boot_media_drivers_hp_enabled"] = false,
            ["boot_media_drivers_custom_enabled"] = true,
            ["boot_media_connect_runtime_payload_source"] = "release",
            ["boot_media_deploy_runtime_payload_source"] = "release",
            ["autopilot_enabled"] = true,
            ["autopilot_provisioning_mode"] = "hardware_hash_upload",
            ["network_configured"] = true,
            ["connect_configured"] = true,
            ["deploy_configured"] = true,
            ["localization_any_enabled"] = true,
            ["localization_visible_languages_count"] = 2,
            ["localization_default_language_configured"] = true,
            ["localization_time_zone_configured"] = true,
            ["network_any_enabled"] = true,
            ["network_wired_dot1x_enabled"] = true,
            ["network_wired_dot1x_profile_configured"] = true,
            ["network_wired_dot1x_certificate_required"] = true,
            ["network_wired_dot1x_certificate_configured"] = true,
            ["network_wifi_provisioning_enabled"] = true,
            ["network_wifi_profile_configured"] = true,
            ["network_wifi_security_type"] = "personal",
            ["network_wifi_ssid_configured"] = true,
            ["network_wifi_passphrase_configured"] = true,
            ["network_wifi_enterprise_profile_configured"] = false,
            ["network_wifi_enterprise_certificate_required"] = false,
            ["network_wifi_enterprise_certificate_configured"] = false,
            ["customization_any_enabled"] = true,
            ["customization_machine_naming_enabled"] = true,
            ["customization_machine_naming_mode"] = "auto_generated_editable",
            ["customization_machine_naming_prefix_configured"] = true,
            ["customization_oobe_enabled"] = true,
            ["customization_oobe_skip_license_terms"] = true,
            ["customization_oobe_diagnostic_data_level"] = "off",
            ["customization_oobe_hide_privacy_setup"] = true,
            ["customization_oobe_tailored_experiences_enabled"] = false,
            ["customization_oobe_advertising_id_enabled"] = false,
            ["customization_oobe_online_speech_recognition_enabled"] = false,
            ["customization_oobe_inking_typing_diagnostics_enabled"] = false,
            ["customization_oobe_location_access"] = "force_off",
            ["customization_appx_removal_enabled"] = true,
            ["customization_appx_removal_package_count"] = 8,
            ["customization_appx_removal_profile"] = "gaming_xbox",
            ["customization_appx_removal_package_names"] = "Microsoft.XboxApp",
            ["customization_ai_component_removal_enabled"] = true,
            ["customization_ai_remove_copilot_enabled"] = true,
            ["customization_ai_remove_ai_hub_enabled"] = true,
            ["customization_ai_disable_recall_enabled"] = true,
            ["customization_ai_disable_click_to_do_enabled"] = true,
            ["customization_ai_disable_service_autostart_enabled"] = true,
            ["customization_ai_disable_edge_ai_enabled"] = true,
            ["customization_ai_disable_paint_ai_enabled"] = true,
            ["customization_ai_disable_notepad_ai_enabled"] = true,
            ["customization_ai_component_removal_option_count"] = 8,
            ["ssid"] = "CorpWifi",
            ["iso_output_path"] = @"C:\Temp\Foundry.iso"
        };

        IReadOnlyDictionary<string, object?> result = TelemetryEventPropertyPolicy.Sanitize(TelemetryEvents.OsdBootMediaFinished, input);

        Assert.Equal("iso", result["boot_media_target"]);
        Assert.True((bool)result["boot_media_creation_success"]!);
        Assert.Equal(12.5, result["boot_media_creation_duration_seconds"]);
        Assert.Equal("x64", result["boot_media_architecture"]);
        Assert.Equal("Prepare WinPE workspace", result["boot_media_creation_failed_step_name"]);
        Assert.Equal("en-us", result["boot_media_winpe_language"]);
        Assert.Equal("winpe_adk", result["boot_media_boot_image_source"]);
        Assert.Equal("signed", result["boot_media_signature_mode"]);
        Assert.Equal("gpt", result["boot_media_usb_partition_style"]);
        Assert.Equal("quick", result["boot_media_usb_format_mode"]);
        Assert.True((bool)result["boot_media_drivers_dell_enabled"]!);
        Assert.False((bool)result["boot_media_drivers_hp_enabled"]!);
        Assert.True((bool)result["boot_media_drivers_custom_enabled"]!);
        Assert.Equal("release", result["boot_media_connect_runtime_payload_source"]);
        Assert.Equal("release", result["boot_media_deploy_runtime_payload_source"]);
        Assert.True((bool)result["autopilot_enabled"]!);
        Assert.Equal("hardware_hash_upload", result["autopilot_provisioning_mode"]);
        Assert.False(result.ContainsKey("network_configured"));
        Assert.False(result.ContainsKey("connect_configured"));
        Assert.False(result.ContainsKey("deploy_configured"));
        Assert.True((bool)result["localization_any_enabled"]!);
        Assert.Equal(2, result["localization_visible_languages_count"]);
        Assert.True((bool)result["localization_default_language_configured"]!);
        Assert.True((bool)result["localization_time_zone_configured"]!);
        Assert.True((bool)result["network_any_enabled"]!);
        Assert.True((bool)result["network_wired_dot1x_enabled"]!);
        Assert.True((bool)result["network_wired_dot1x_profile_configured"]!);
        Assert.True((bool)result["network_wired_dot1x_certificate_required"]!);
        Assert.True((bool)result["network_wired_dot1x_certificate_configured"]!);
        Assert.True((bool)result["network_wifi_provisioning_enabled"]!);
        Assert.True((bool)result["network_wifi_profile_configured"]!);
        Assert.Equal("personal", result["network_wifi_security_type"]);
        Assert.True((bool)result["network_wifi_ssid_configured"]!);
        Assert.True((bool)result["network_wifi_passphrase_configured"]!);
        Assert.False((bool)result["network_wifi_enterprise_profile_configured"]!);
        Assert.False((bool)result["network_wifi_enterprise_certificate_required"]!);
        Assert.False((bool)result["network_wifi_enterprise_certificate_configured"]!);
        Assert.True((bool)result["customization_any_enabled"]!);
        Assert.True((bool)result["customization_machine_naming_enabled"]!);
        Assert.Equal("auto_generated_editable", result["customization_machine_naming_mode"]);
        Assert.True((bool)result["customization_machine_naming_prefix_configured"]!);
        Assert.True((bool)result["customization_oobe_enabled"]!);
        Assert.True((bool)result["customization_oobe_skip_license_terms"]!);
        Assert.Equal("off", result["customization_oobe_diagnostic_data_level"]);
        Assert.True((bool)result["customization_oobe_hide_privacy_setup"]!);
        Assert.False((bool)result["customization_oobe_tailored_experiences_enabled"]!);
        Assert.False((bool)result["customization_oobe_advertising_id_enabled"]!);
        Assert.False((bool)result["customization_oobe_online_speech_recognition_enabled"]!);
        Assert.False((bool)result["customization_oobe_inking_typing_diagnostics_enabled"]!);
        Assert.Equal("force_off", result["customization_oobe_location_access"]);
        Assert.True((bool)result["customization_appx_removal_enabled"]!);
        Assert.Equal(8, result["customization_appx_removal_package_count"]);
        Assert.Equal("gaming_xbox", result["customization_appx_removal_profile"]);
        Assert.True((bool)result["customization_ai_component_removal_enabled"]!);
        Assert.True((bool)result["customization_ai_remove_copilot_enabled"]!);
        Assert.True((bool)result["customization_ai_remove_ai_hub_enabled"]!);
        Assert.True((bool)result["customization_ai_disable_recall_enabled"]!);
        Assert.True((bool)result["customization_ai_disable_click_to_do_enabled"]!);
        Assert.True((bool)result["customization_ai_disable_service_autostart_enabled"]!);
        Assert.True((bool)result["customization_ai_disable_edge_ai_enabled"]!);
        Assert.True((bool)result["customization_ai_disable_paint_ai_enabled"]!);
        Assert.True((bool)result["customization_ai_disable_notepad_ai_enabled"]!);
        Assert.Equal(8, result["customization_ai_component_removal_option_count"]);
        Assert.False(result.ContainsKey("customization_appx_removal_package_names"));
        Assert.False(result.ContainsKey("ssid"));
        Assert.False(result.ContainsKey("iso_output_path"));
    }

    [Fact]
    public void Sanitize_ForDeploySessionFinished_DropsKnownDeploySensitiveValues()
    {
        Dictionary<string, object?> input = new()
        {
            ["boot_media_target"] = "iso",
            ["deploy_runtime_payload_source"] = "release",
            ["deploy_session_success"] = false,
            ["deploy_session_cancelled"] = false,
            ["deploy_session_duration_seconds"] = 30,
            ["deploy_session_completed_step_count"] = 4,
            ["deploy_session_failed_step_name"] = "ApplyOperatingSystemImage",
            ["deploy_session_mode"] = "iso",
            ["deploy_session_dry_run_enabled"] = false,
            ["deploy_hardware_vendor"] = "dell",
            ["deploy_hardware_model"] = "latitude 5450",
            ["deploy_hardware_virtual_machine"] = false,
            ["deploy_os_product"] = "windows_11",
            ["deploy_os_version"] = "24h2",
            ["deploy_os_build"] = "26100",
            ["deploy_os_architecture"] = "x64",
            ["deploy_os_language"] = "en-us",
            ["deploy_driver_pack_selection_kind"] = "oemcatalog",
            ["deploy_driver_pack_vendor"] = "dell",
            ["deploy_driver_pack_model"] = "latitude 5450",
            ["deploy_firmware_updates_enabled"] = true,
            ["deploy_autopilot_enabled"] = true,
            ["deploy_autopilot_provisioning_mode"] = "hardware_hash_upload",
            ["deploy_autopilot_hash_upload_state"] = "completed",
            ["deploy_autopilot_hash_group_tag_selected"] = true,
            ["operating_system_url"] = "https://example.invalid/os.wim",
            ["driver_pack_url"] = "https://example.invalid/driver.cab",
            ["target_computer_name"] = "PC-001",
            ["tenant_id"] = "tenant-id",
            ["certificate_thumbprint"] = "ABCDEF",
            ["serial_number"] = "SERIAL",
            ["hardware_hash"] = "HASH",
            ["group_tag"] = "KIOSK",
            ["exception"] = @"C:\Temp\failure.log"
        };

        IReadOnlyDictionary<string, object?> result = TelemetryEventPropertyPolicy.Sanitize(TelemetryEvents.DeploySessionFinished, input);

        Assert.False(result.ContainsKey("boot_media_target"));
        Assert.False(result.ContainsKey("deploy_runtime_payload_source"));
        Assert.Equal(false, result["deploy_session_success"]);
        Assert.Equal("ApplyOperatingSystemImage", result["deploy_session_failed_step_name"]);
        Assert.Equal("iso", result["deploy_session_mode"]);
        Assert.Equal("windows_11", result["deploy_os_product"]);
        Assert.Equal("dell", result["deploy_driver_pack_vendor"]);
        Assert.Equal("latitude 5450", result["deploy_driver_pack_model"]);
        Assert.True((bool)result["deploy_firmware_updates_enabled"]!);
        Assert.True((bool)result["deploy_autopilot_enabled"]!);
        Assert.Equal("hardware_hash_upload", result["deploy_autopilot_provisioning_mode"]);
        Assert.Equal("completed", result["deploy_autopilot_hash_upload_state"]);
        Assert.True((bool)result["deploy_autopilot_hash_group_tag_selected"]!);
        Assert.False(result.ContainsKey("operating_system_url"));
        Assert.False(result.ContainsKey("driver_pack_url"));
        Assert.False(result.ContainsKey("target_computer_name"));
        Assert.False(result.ContainsKey("tenant_id"));
        Assert.False(result.ContainsKey("certificate_thumbprint"));
        Assert.False(result.ContainsKey("serial_number"));
        Assert.False(result.ContainsKey("hardware_hash"));
        Assert.False(result.ContainsKey("group_tag"));
        Assert.False(result.ContainsKey("exception"));
    }

    [Fact]
    public void Sanitize_ForConnectSessionReady_AllowsLayoutMode()
    {
        Dictionary<string, object?> input = new()
        {
            ["boot_media_target"] = "usb",
            ["connect_runtime_payload_source"] = "debug",
            ["connect_network_connection_type"] = "ethernet",
            ["connect_network_layout_mode"] = "ethernet_wifi",
            ["connect_ethernet_available"] = true,
            ["connect_wifi_available"] = true,
            ["connect_wifi_security_type"] = "none",
            ["connect_wifi_source"] = "none",
            ["connect_wired_dot1x_enabled"] = true,
            ["connect_wifi_provisioned"] = true,
            ["adapter_name"] = "Ethernet 1"
        };

        IReadOnlyDictionary<string, object?> result = TelemetryEventPropertyPolicy.Sanitize(TelemetryEvents.ConnectSessionReady, input);

        Assert.False(result.ContainsKey("boot_media_target"));
        Assert.False(result.ContainsKey("connect_runtime_payload_source"));
        Assert.Equal("ethernet", result["connect_network_connection_type"]);
        Assert.Equal("ethernet_wifi", result["connect_network_layout_mode"]);
        Assert.True((bool)result["connect_ethernet_available"]!);
        Assert.True((bool)result["connect_wifi_available"]!);
        Assert.Equal("none", result["connect_wifi_security_type"]);
        Assert.Equal("none", result["connect_wifi_source"]);
        Assert.True((bool)result["connect_wired_dot1x_enabled"]!);
        Assert.True((bool)result["connect_wifi_provisioned"]!);
        Assert.False(result.ContainsKey("adapter_name"));
        Assert.False(result.ContainsKey("success"));
    }

    [Fact]
    public void IsKnownEvent_ReturnsFalseForOldAndUnknownEventNames()
    {
        Assert.True(TelemetryEventPropertyPolicy.IsKnownEvent(TelemetryEvents.AppDailyActive));
        Assert.True(TelemetryEventPropertyPolicy.IsKnownEvent(TelemetryEvents.OsdBootMediaFinished));
        Assert.True(TelemetryEventPropertyPolicy.IsKnownEvent(TelemetryEvents.ConnectSessionReady));
        Assert.True(TelemetryEventPropertyPolicy.IsKnownEvent(TelemetryEvents.DeploySessionFinished));

        Assert.False(TelemetryEventPropertyPolicy.IsKnownEvent("app_started"));
        Assert.False(TelemetryEventPropertyPolicy.IsKnownEvent("boot_media_created"));
        Assert.False(TelemetryEventPropertyPolicy.IsKnownEvent("connect_network_ready"));
        Assert.False(TelemetryEventPropertyPolicy.IsKnownEvent("deployment_completed"));
        Assert.False(TelemetryEventPropertyPolicy.IsKnownEvent("unknown_event"));
    }
}
