namespace Foundry.Telemetry;

/// <summary>
/// Applies explicit per-event property allowlists before telemetry leaves the process.
/// </summary>
public static class TelemetryEventPropertyPolicy
{
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
        "username",
        "domain",
        "email",
        "ip_address"
    };

    private static readonly IReadOnlyDictionary<string, HashSet<string>> AllowedPropertiesByEvent =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            [TelemetryEvents.AppDailyActive] = new(StringComparer.Ordinal),
            [TelemetryEvents.OsdBootMediaFinished] = new(StringComparer.Ordinal)
            {
                "boot_media_target",
                "success",
                "duration_seconds",
                "failed_step_name",
                "boot_media_architecture",
                "winpe_language",
                "boot_image_source",
                "signature_mode",
                "usb_partition_style",
                "usb_format_mode",
                "include_dell_drivers",
                "include_hp_drivers",
                "custom_drivers_enabled",
                "network_configured",
                "connect_configured",
                "deploy_configured",
                "connect_runtime_payload_source",
                "deploy_runtime_payload_source",
                "autopilot_enabled"
            },
            [TelemetryEvents.ConnectSessionReady] = new(StringComparer.Ordinal)
            {
                "success",
                "connection_type",
                "layout_mode",
                "wifi_security",
                "wifi_source",
                "wired_dot1x_enabled",
                "wifi_provisioned"
            },
            [TelemetryEvents.DeploySessionFinished] = new(StringComparer.Ordinal)
            {
                "success",
                "cancelled",
                "duration_seconds",
                "completed_step_count",
                "failed_step_name",
                "mode",
                "is_dry_run",
                "hardware_vendor",
                "hardware_model",
                "is_virtual_machine",
                "os_product",
                "os_version",
                "os_build",
                "os_architecture",
                "os_language",
                "driver_pack_selection_kind",
                "driver_pack_vendor",
                "driver_pack_model",
                "firmware_updates_enabled",
                "autopilot_enabled"
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
    /// Returns only properties explicitly allowed for the supplied event.
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

        Dictionary<string, object?> sanitized = new(StringComparer.Ordinal);
        foreach ((string key, object? value) in properties)
        {
            if (!allowedProperties.Contains(key) || IsSensitiveKey(key))
            {
                continue;
            }

            sanitized[key] = value;
        }

        return sanitized;
    }

    private static bool IsSensitiveKey(string key)
    {
        return SensitiveKeys.Contains(key) ||
            key.Contains("path", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("url", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("exception", StringComparison.OrdinalIgnoreCase);
    }
}
