namespace Foundry.Core.Services.WinPe;

public sealed record WinPeWorkspacePreparationOptions
{
    public WinPeBuildArtifact? Artifact { get; init; }
    public WinPeToolPaths? Tools { get; init; }
    public WinPeSignatureMode SignatureMode { get; init; } = WinPeSignatureMode.Pca2011;
    public WinPeBootImageSource BootImageSource { get; init; } = WinPeBootImageSource.WinPe;
    public string DriverCatalogUri { get; init; } = string.Empty;
    public IReadOnlyList<WinPeVendorSelection> DriverVendors { get; init; } = [];
    public string? CustomDriverDirectoryPath { get; init; }
    public string WinPeLanguage { get; init; } = string.Empty;
    public WinPeMountedImageAssetProvisioningOptions? AssetProvisioning { get; init; }
    public WinPeRuntimePayloadProvisioningOptions? RuntimePayloadProvisioning { get; init; }
    public string WinReCacheDirectoryPath { get; init; } = string.Empty;
    public Uri? WinReCatalogUri { get; init; }
    public IProgress<WinPeWorkspacePreparationStage>? Progress { get; init; }
    public IProgress<WinPeMountedImageCustomizationProgress>? CustomizationProgress { get; init; }
}
