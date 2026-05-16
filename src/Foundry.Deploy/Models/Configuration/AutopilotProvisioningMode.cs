using System.Text.Json.Serialization;

namespace Foundry.Deploy.Models.Configuration;

/// <summary>
/// Defines how Foundry.Deploy should provision Autopilot data during OS deployment.
/// </summary>
[JsonConverter(typeof(AutopilotProvisioningModeJsonConverter))]
public enum AutopilotProvisioningMode
{
    /// <summary>
    /// Stages an offline Autopilot profile JSON file into the applied Windows image.
    /// </summary>
    JsonProfile,

    /// <summary>
    /// Captures and uploads the device hardware hash from WinPE.
    /// </summary>
    HardwareHashUpload
}
