namespace Foundry.Deploy.Models.Configuration;

/// <summary>
/// Defines the Windows diagnostic data level applied by Foundry.Deploy before OOBE starts.
/// </summary>
public enum DeployOobeDiagnosticDataLevel
{
    /// <summary>
    /// Sends the minimum diagnostic data required to keep Windows secure, up to date, and working properly.
    /// </summary>
    Required,

    /// <summary>
    /// Sends required data plus optional diagnostic data about device health, app activity, and enhanced error reports.
    /// </summary>
    Optional,

    /// <summary>
    /// Requests diagnostic data collection to be turned off where the installed Windows edition supports it.
    /// </summary>
    Off
}
