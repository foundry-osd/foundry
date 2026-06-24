// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Models;

public sealed record AutopilotProfileCatalogItem
{
    public required string FolderName { get; init; }
    public required string DisplayName { get; init; }
    public required string ConfigurationFilePath { get; init; }
}
