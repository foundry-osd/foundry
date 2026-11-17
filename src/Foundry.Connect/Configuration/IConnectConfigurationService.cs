// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Runtime;

namespace Foundry.Connect.Configuration;

/// <summary>
/// Loads and normalizes the Foundry.Connect runtime configuration.
/// </summary>
public interface IConnectConfigurationService
{
    /// <summary>
    /// Gets the resolved configuration file path when one was used.
    /// </summary>
    string? ConfigurationPath { get; }

    /// <summary>
    /// Gets whether configuration was loaded from disk instead of built-in defaults.
    /// </summary>
    bool IsLoadedFromDisk { get; }

    /// <summary>
    /// Gets whether the loaded boot media should be updated for best compatibility.
    /// </summary>
    bool IsBootMediaUpdateRecommended { get; }

    /// <summary>Gets why rebuilding this boot media is recommended.</summary>
    BootMediaUpdateReason BootMediaUpdateReason { get; }

    /// <summary>
    /// Loads the Connect view of <c>foundry.winpe.config.json</c> from the override or the booted media.
    /// </summary>
    /// <returns>
    /// The normalized runtime configuration with its session-only secrets, or a failure message. Configuration problems
    /// never throw.
    /// </returns>
    ConnectConfigurationLoadResult Load();
}
