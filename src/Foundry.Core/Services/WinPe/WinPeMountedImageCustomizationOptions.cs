namespace Foundry.Core.Services.WinPe;

public sealed record WinPeMountedImageCustomizationOptions
{
    public WinPeBuildArtifact? Artifact { get; init; }
    public WinPeToolPaths? Tools { get; init; }
    public WinPeBootImageSource BootImageSource { get; init; } = WinPeBootImageSource.WinPe;
    public string WinPeLanguage { get; init; } = string.Empty;
    public IReadOnlyList<string> DriverPackagePaths { get; init; } = [];
    public WinPeMountedImageAssetProvisioningOptions? AssetProvisioning { get; init; }
    public WinPeRuntimePayloadProvisioningOptions? RuntimePayloadProvisioning { get; init; }
    public string WinReCacheDirectoryPath { get; init; } = string.Empty;
    public Uri? WinReCatalogUri { get; init; }
    public IProgress<WinPeDownloadProgress>? DownloadProgress { get; init; }
    public IProgress<WinPeMountedImageCustomizationProgress>? Progress { get; init; }
}
