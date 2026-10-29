// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

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
    /// Loads configuration from command-line, environment, or WinPE media locations.
    /// </summary>
    /// <returns>
    /// The normalized runtime configuration with the session-only secrets decrypted from its envelopes or read
    /// from legacy plaintext media, or a failure message. Configuration problems never throw.
    /// </returns>
    ConnectConfigurationLoadResult Load();
}
