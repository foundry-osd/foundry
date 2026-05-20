namespace Foundry.Deploy.Services.Runtime;

/// <summary>
/// Selects the in-memory Autopilot mode used by Foundry.Deploy debug safe mode.
/// </summary>
public enum DebugAutopilotMode
{
    /// <summary>
    /// Disables Autopilot during the debug deployment run.
    /// </summary>
    None,

    /// <summary>
    /// Simulates JSON profile provisioning during the debug deployment run.
    /// </summary>
    JsonProfile,

    /// <summary>
    /// Simulates hardware hash upload provisioning during the debug deployment run.
    /// </summary>
    HardwareHashUpload
}
