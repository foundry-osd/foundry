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

    /// <summary>
    /// Gets the selected catalog driver pack when a catalog-backed strategy is used.
    /// </summary>
    public DriverPackCatalogItem? DriverPack { get; init; }

    /// <summary>
    /// Gets a value indicating whether matching firmware updates should be downloaded and applied.
    /// </summary>
    public bool ApplyFirmwareUpdates { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether an Autopilot profile should be staged for OOBE.
    /// </summary>
    public bool IsAutopilotEnabled { get; init; }

    /// <summary>
    /// Gets the selected Autopilot profile staged into the offline Windows image.
    /// </summary>
    public AutopilotProfileCatalogItem? SelectedAutopilotProfile { get; init; }

    /// <summary>
    /// Gets a value indicating whether deployment runs against a temporary workspace instead of mutating a target disk.
    /// </summary>
    public bool IsDryRun { get; init; }
}
