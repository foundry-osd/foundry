// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Configuration;

/// <summary>
/// Loads optional Foundry.Deploy configuration staged by Foundry OSD.
/// </summary>
public interface IDeployConfigurationService
{
    /// <summary>
    /// Loads configuration when present, otherwise returns defaults.
    /// </summary>
    /// <returns>The configuration load result.</returns>
    DeployConfigurationLoadResult LoadOptional();
}
