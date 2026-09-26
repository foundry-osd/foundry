// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Models;

/// <summary>Identifies a downloadable catalog image and its target metadata.</summary>
public sealed record OperatingSystemCatalogItem : OperatingSystemMetadata
{
    public string Url { get; init; } = string.Empty;
    public string Sha1 { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
}
