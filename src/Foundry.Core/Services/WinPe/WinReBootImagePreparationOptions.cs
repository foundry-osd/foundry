// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public sealed record WinReBootImagePreparationOptions
{
    public required WinPeBuildArtifact Artifact { get; init; }
    public required WinPeToolPaths Tools { get; init; }
    public required string WinPeLanguage { get; init; }
    public required string CacheDirectoryPath { get; init; }
    public Uri CatalogUri { get; init; } = WinReBootImagePreparationService.DefaultOperatingSystemCatalogUri;
    public IProgress<WinPeDownloadProgress>? DownloadProgress { get; init; }
    public IProgress<WinPeMountedImageCustomizationProgress>? Progress { get; init; }
}
