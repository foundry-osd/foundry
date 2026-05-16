namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Defines how Windows location access is configured before first sign-in.
/// </summary>
public enum OobeLocationAccessMode
{
    /// <summary>
    /// Leaves location choices available to the user after setup.
    /// </summary>
    UserControlled,

    /// <summary>
    /// Turns location services off and prevents apps from using device location.
    /// </summary>
    ForceOff
}
