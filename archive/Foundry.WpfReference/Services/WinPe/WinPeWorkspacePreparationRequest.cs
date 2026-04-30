using Foundry.Models.Configuration;

namespace Foundry.Services.WinPe;

internal sealed record WinPeWorkspacePreparationRequest
{
    public required WinPeBuildArtifact Artifact { get; init; }

    public required WinPeToolPaths Tools { get; init; }

    public required string DriverCatalogUri { get; init; }

    public required IReadOnlyList<WinPeVendorSelection> DriverVendors { get; init; }

    public string? CustomDriverDirectoryPath { get; init; }

    public required WinPeSignatureMode SignatureMode { get; init; }
    public WinPeBootImageSource BootImageSource { get; init; } = WinPeBootImageSource.WinPe;

    public required string WinPeLanguage { get; init; }
    public IProgress<WinPeMountedImageCustomizationProgress>? CustomizationProgress { get; init; }

    public string? FoundryConnectConfigurationJson { get; init; }
    public IReadOnlyList<FoundryConnectProvisionedAssetFile> FoundryConnectAssetFiles { get; init; } = Array.Empty<FoundryConnectProvisionedAssetFile>();
    public string? ExpertDeployConfigurationJson { get; init; }
    public IReadOnlyList<AutopilotProfileSettings> AutopilotProfiles { get; init; } = Array.Empty<AutopilotProfileSettings>();
}
