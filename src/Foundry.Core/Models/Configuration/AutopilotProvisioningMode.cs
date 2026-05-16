using System.Text.Json.Serialization;

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Defines how Foundry provisions Autopilot data during OS deployment.
/// </summary>
[JsonConverter(typeof(AutopilotProvisioningModeJsonConverter))]
public enum AutopilotProvisioningMode
{
    /// <summary>
    /// Stages an offline Autopilot profile JSON file into the applied Windows image.
    /// </summary>
    JsonProfile,

    /// <summary>
    /// Captures and uploads the device hardware hash from WinPE using the configured tenant app registration.
    /// </summary>
    HardwareHashUpload
}
