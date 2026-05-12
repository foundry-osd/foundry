namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>
/// Defines the stable execution buckets for pre-OOBE PowerShell customizations.
/// </summary>
public enum PreOobeScriptPriority
{
    /// <summary>
    /// Runs driver provisioning before other customization scripts.
    /// </summary>
    DriverProvisioning = 100,

    /// <summary>
    /// Runs general Windows customization scripts after driver provisioning.
    /// </summary>
    Customization = 300,

    /// <summary>
    /// Runs validation scripts after customization.
    /// </summary>
    Validation = 800,

    /// <summary>
    /// Runs cleanup scripts at the end of the pre-OOBE sequence.
    /// </summary>
    Cleanup = 900
}
