using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Contains the immutable deployment request selected by the wizard.
/// </summary>
public sealed record DeploymentContext
{
    /// <summary>
    /// Gets the deployment source mode.
    /// </summary>
    public required DeploymentMode Mode { get; init; }

    /// <summary>
    /// Gets the requested cache root path.
    /// </summary>
    public required string CacheRootPath { get; init; }

    /// <summary>
    /// Gets the target disk number selected for deployment.
    /// </summary>
    public required int TargetDiskNumber { get; init; }

    /// <summary>
    /// Gets the target computer name written into unattend.xml.
    /// </summary>
    public required string TargetComputerName { get; init; }

    /// <summary>
    /// Gets the optional default Windows time zone ID written into unattend.xml.
    /// </summary>
    public string? DefaultTimeZoneId { get; init; }

    /// <summary>
    /// Gets the operating system catalog item to download and apply.
    /// </summary>
    public required OperatingSystemCatalogItem OperatingSystem { get; init; }

    /// <summary>
    /// Gets the selected driver pack strategy.
    /// </summary>
    public required DriverPackSelectionKind DriverPackSelectionKind { get; init; }
    public DriverPackCatalogItem? DriverPack { get; init; }
    public bool ApplyFirmwareUpdates { get; init; } = true;
    public bool IsAutopilotEnabled { get; init; }
    public AutopilotProfileCatalogItem? SelectedAutopilotProfile { get; init; }
    public bool IsDryRun { get; init; }
}
