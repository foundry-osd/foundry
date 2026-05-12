namespace Foundry.Connect.Models.Configuration;

/// <summary>
/// Identifies which credential context a provisioned enterprise network profile expects.
/// </summary>
public enum NetworkAuthenticationMode
{
    /// <summary>
    /// The profile authenticates with machine credentials only.
    /// </summary>
    MachineOnly = 0,

    /// <summary>
    /// The profile authenticates with user credentials only.
    /// </summary>
    UserOnly = 1,

    /// <summary>
    /// The profile may authenticate with either machine or user credentials.
    /// </summary>
    MachineOrUser = 2
}
