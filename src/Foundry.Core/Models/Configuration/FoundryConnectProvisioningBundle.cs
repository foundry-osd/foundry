// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Represents the <c>connect</c> section of a media build and the network assets staged for it.
/// </summary>
public sealed record FoundryConnectProvisioningBundle
{
    /// <summary>
    /// Gets the generated <c>connect</c> section of the WinPE runtime configuration.
    /// </summary>
    public ConnectRuntimeSettings Configuration { get; init; } = new();

    /// <summary>
    /// Gets additional asset files staged beside the configuration.
    /// </summary>
    public IReadOnlyList<FoundryConnectProvisionedAssetFile> AssetFiles { get; init; } = Array.Empty<FoundryConnectProvisionedAssetFile>();
}
