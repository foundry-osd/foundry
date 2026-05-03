namespace Foundry.Core.Services.WinPe;

public sealed record WinReBootImagePreparationOptions
{
    public required WinPeBuildArtifact Artifact { get; init; }
    public required WinPeToolPaths Tools { get; init; }
    public required string WinPeLanguage { get; init; }
    public required string CacheDirectoryPath { get; init; }
    public Uri CatalogUri { get; init; } = WinReBootImagePreparationService.DefaultOperatingSystemCatalogUri;
}
