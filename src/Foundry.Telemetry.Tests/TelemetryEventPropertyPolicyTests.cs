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
            ["success"] = true,
            ["duration_seconds"] = 12.5,
            ["boot_media_architecture"] = "x64",
            ["failed_step_name"] = "Prepare WinPE workspace",
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
            ["ssid"] = "CorpWifi",
            ["iso_output_path"] = @"C:\Temp\Foundry.iso"
        };

        IReadOnlyDictionary<string, object?> result = TelemetryEventPropertyPolicy.Sanitize(TelemetryEvents.OsdBootMediaFinished, input);

        Assert.Equal("iso", result["boot_media_target"]);
        Assert.True((bool)result["success"]!);
        Assert.Equal(12.5, result["duration_seconds"]);
        Assert.Equal("x64", result["boot_media_architecture"]);
        Assert.Equal("Prepare WinPE workspace", result["failed_step_name"]);
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
        Assert.False(result.ContainsKey("ssid"));
        Assert.False(result.ContainsKey("iso_output_path"));
    }

    [Fact]
    public void Sanitize_ForDeploySessionFinished_DropsKnownDeploySensitiveValues()
    {
        Dictionary<string, object?> input = new()
        {
            ["success"] = false,
            ["cancelled"] = false,
            ["duration_seconds"] = 30,
            ["completed_step_count"] = 4,
            ["failed_step_name"] = "ApplyOperatingSystemImage",
            ["operating_system_url"] = "https://example.invalid/os.wim",
            ["driver_pack_url"] = "https://example.invalid/driver.cab",
            ["target_computer_name"] = "PC-001",
            ["exception"] = @"C:\Temp\failure.log"
        };

        IReadOnlyDictionary<string, object?> result = TelemetryEventPropertyPolicy.Sanitize(TelemetryEvents.DeploySessionFinished, input);

        Assert.Equal(false, result["success"]);
        Assert.Equal("ApplyOperatingSystemImage", result["failed_step_name"]);
        Assert.False(result.ContainsKey("operating_system_url"));
        Assert.False(result.ContainsKey("driver_pack_url"));
        Assert.False(result.ContainsKey("target_computer_name"));
        Assert.False(result.ContainsKey("exception"));
    }

    [Fact]
    public void Sanitize_ForConnectSessionReady_AllowsLayoutMode()
    {
        Dictionary<string, object?> input = new()
        {
            ["success"] = true,
            ["connection_type"] = "ethernet",
            ["layout_mode"] = "ethernet_wifi",
            ["adapter_name"] = "Ethernet 1"
        };

        IReadOnlyDictionary<string, object?> result = TelemetryEventPropertyPolicy.Sanitize(TelemetryEvents.ConnectSessionReady, input);

        Assert.True((bool)result["success"]!);
        Assert.Equal("ethernet", result["connection_type"]);
        Assert.Equal("ethernet_wifi", result["layout_mode"]);
        Assert.False(result.ContainsKey("adapter_name"));
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
