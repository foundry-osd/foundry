namespace Foundry.Deploy.Services.DriverPacks;

/// <summary>
/// Identifies the deferred pre-OOBE command used to install a staged driver package.
/// </summary>
public enum DeferredDriverPackageCommandKind
{
    /// <summary>
    /// No deferred command is required.
    /// </summary>
    None = 0,

    /// <summary>
    /// Run a Lenovo driver pack executable during pre-OOBE.
    /// </summary>
    LenovoExecutable = 1,

    /// <summary>
    /// Run a Microsoft Surface MSI package during pre-OOBE.
    /// </summary>
    SurfaceMsi = 2
}
