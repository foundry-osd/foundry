// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>Binds runtime defaults to a specific external media manifest, never a host library path.</summary>
public sealed record DeployCustomImagesSettings
{
    public bool IsEnabled { get; init; }
    public CustomImageSource DefaultSource { get; init; }
    public string? DefaultImageId { get; init; }
    public int? DefaultImageIndex { get; init; }
    public string? ManifestId { get; init; }
    public string? ManifestHash { get; init; }
}
