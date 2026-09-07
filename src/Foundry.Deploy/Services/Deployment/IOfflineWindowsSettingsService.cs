// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models.Configuration;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Defines offline Windows settings operations.</summary>
public interface IOfflineWindowsSettingsService
{
    /// <summary>
    /// Writes computer name and optional time zone into Windows\Panther\unattend.xml.
    /// </summary>
    /// <param name="windowsPartitionRoot">Root path of the target Windows partition.</param>
    /// <param name="computerName">Computer name written into unattend.xml.</param>
    /// <param name="processorArchitecture">Processor architecture used by unattend components.</param>
    /// <param name="defaultTimeZoneId">Optional Windows time-zone identifier written into unattend.xml.</param>
    /// <param name="cancellationToken">Token that cancels unattend generation.</param>
    /// <returns>A task that completes after unattend.xml is written.</returns>
    Task ConfigureOfflineComputerNameAsync(
        string windowsPartitionRoot,
        string computerName,
        string processorArchitecture,
        string? defaultTimeZoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes Windows OOBE unattend and policy settings into an offline Windows installation.
    /// </summary>
    /// <param name="windowsPartitionRoot">Root path of the target Windows partition.</param>
    /// <param name="settings">The OOBE customization settings generated from the Foundry configuration.</param>
    /// <param name="processorArchitecture">Processor architecture used by unattend components.</param>
    /// <param name="workingDirectory">Directory used for temporary command output.</param>
    /// <param name="workspaceRootPath">Deployment workspace root containing encrypted secret material.</param>
    /// <param name="cancellationToken">Token that cancels OOBE configuration.</param>
    /// <returns>A task that completes after OOBE settings are written.</returns>
    Task ConfigureOfflineOobeAsync(
        string windowsPartitionRoot,
        DeployOobeSettings settings,
        string processorArchitecture,
        string workingDirectory,
        string workspaceRootPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes Windows AI component policy settings into an offline Windows installation.
    /// </summary>
    /// <param name="windowsPartitionRoot">Root path of the target Windows partition.</param>
    /// <param name="settings">The AI component removal settings generated from the Foundry configuration.</param>
    /// <param name="workingDirectory">Directory used for temporary command output.</param>
    /// <param name="cancellationToken">Token that cancels AI policy configuration.</param>
    /// <returns>A task that completes after selected AI policy settings are written.</returns>
    Task ConfigureOfflineAiComponentRemovalAsync(
        string windowsPartitionRoot,
        DeployAiComponentRemovalSettings settings,
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies Windows optional feature changes to the offline Windows installation.
    /// </summary>
    /// <param name="setupMediaImagePath">Path to the setup-media ESD.</param>
    /// <param name="windowsPartitionRoot">Root path of the target Windows partition.</param>
    /// <param name="appliedImageIndex">Index of the Windows image applied from the setup media.</param>
    /// <param name="settings">Optional feature actions generated from the Foundry configuration.</param>
    /// <param name="scratchDirectory">DISM scratch directory.</param>
    /// <param name="sourceExtractionDirectory">Directory used to extract setup-media sources.</param>
    /// <param name="workingDirectory">Directory used for temporary command output.</param>
    /// <param name="cancellationToken">Token that cancels optional feature servicing.</param>
    /// <param name="progress">Optional progress sink for DISM percentage updates.</param>
    /// <param name="onInspectionStarted">Optional callback invoked before feature-state inspection.</param>
    /// <param name="onSourcePreparationStarted">Optional callback invoked before setup-media extraction.</param>
    /// <param name="onServicingStarted">Optional callback invoked before feature servicing.</param>
    /// <returns>The optional feature servicing result.</returns>
    Task<WindowsOptionalFeatureServicingResult> ConfigureOfflineWindowsOptionalFeaturesAsync(
        string setupMediaImagePath,
        string windowsPartitionRoot,
        int appliedImageIndex,
        DeployWindowsOptionalFeatureSettings settings,
        string scratchDirectory,
        string sourceExtractionDirectory,
        string workingDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null,
        Action? onInspectionStarted = null,
        Action? onSourcePreparationStarted = null,
        Action? onServicingStarted = null);

}
