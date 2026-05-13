using Foundry.Telemetry;

namespace Foundry.Telemetry.Tests;

public sealed class TelemetryEventPropertyPolicyTests
{
    [Fact]
    public void Sanitize_DropsPropertiesOutsideEventAllowlist()
    {
        Dictionary<string, object?> input = new()
        {
            ["target"] = "iso",
            ["success"] = true,
            ["duration_seconds"] = 12.5,
            ["ssid"] = "CorpWifi",
            ["iso_output_path"] = @"C:\Temp\Foundry.iso"
        };

        IReadOnlyDictionary<string, object?> result = TelemetryEventPropertyPolicy.Sanitize("boot_media_created", input);

        Assert.Equal("iso", result["target"]);
        Assert.True((bool)result["success"]!);
        Assert.Equal(12.5, result["duration_seconds"]);
        Assert.False(result.ContainsKey("ssid"));
        Assert.False(result.ContainsKey("iso_output_path"));
    }

    [Fact]
    public void Sanitize_DropsKnownDeploySensitiveValues()
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

        IReadOnlyDictionary<string, object?> result = TelemetryEventPropertyPolicy.Sanitize("deployment_completed", input);

        Assert.Equal(false, result["success"]);
        Assert.Equal("ApplyOperatingSystemImage", result["failed_step_name"]);
        Assert.False(result.ContainsKey("operating_system_url"));
        Assert.False(result.ContainsKey("driver_pack_url"));
        Assert.False(result.ContainsKey("target_computer_name"));
        Assert.False(result.ContainsKey("exception"));
    }
}
