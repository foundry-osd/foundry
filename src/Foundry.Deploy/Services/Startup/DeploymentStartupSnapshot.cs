// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using BootMediaUpdateReason = Foundry.Core.Models.Configuration.BootMediaUpdateReason;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.System;

namespace Foundry.Deploy.Services.Startup;

public sealed record DeploymentStartupSnapshot
{
    /// <summary>Locates the protected answer-file catalog relative to the loaded runtime configuration.</summary>
    public string ConfigurationPath { get; init; } = Services.Configuration.DeployConfigurationService.DefaultConfigurationPath;
    /// <summary>Preserves a configuration failure instead of falling back to native customization.</summary>
    public string? ConfigurationFailureMessage { get; init; }

    public required string CacheRootPath { get; init; }

    /// <summary>
    /// Gets the configuration every consumer uses. With an active network cache it already carries the custom image, custom
    /// driver and post-installation sections of the verified index published on the share.
    /// </summary>
    public required FoundryDeployConfigurationDocument? DeployConfigurationDocument { get; init; }

    /// <summary>Gets why the network cache prevents the deployment from starting, or <see langword="null"/> when it does not.</summary>
    public NetworkCacheStartupFailure? NetworkCacheFailure { get; init; }

    /// <summary>Gets why rebuilding this boot media is recommended.</summary>
    public BootMediaUpdateReason BootMediaUpdateReason { get; init; }
    public required IReadOnlyList<AutopilotProfileCatalogItem> AutopilotProfiles { get; init; }
    public required MachineNamePreparationResult MachineNamePreparation { get; init; }
    public required HardwareProfile? DetectedHardware { get; init; }
    public required string? HardwareDetectionFailureMessage { get; init; }
    public required IReadOnlyList<TargetDiskInfo> TargetDisks { get; init; }
    public required DeploymentCatalogSnapshot CatalogSnapshot { get; init; }
}
