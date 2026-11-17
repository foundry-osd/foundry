// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Represents the generated WinPE runtime configuration of one media build and the network assets staged beside it.
/// </summary>
public sealed record WinPeConfigurationBundle
{
    /// <summary>Gets the generated configuration model.</summary>
    public FoundryWinPeConfigurationDocument Configuration { get; init; } = new();

    /// <summary>Gets the serialized configuration, before media preparation binds and splits its <c>deploy</c> node.</summary>
    public string ConfigurationJson { get; init; } = string.Empty;

    /// <summary>Gets the network profiles and certificates staged for <c>X:\Foundry\Config\Network</c>.</summary>
    public IReadOnlyList<FoundryConnectProvisionedAssetFile> ConnectAssetFiles { get; init; } = [];
}
