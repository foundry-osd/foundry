using Foundry.Models.Configuration;

namespace Foundry.Services.WinPe;

internal sealed record WinPeMountedImageCustomizationRequest
{
    public required WinPeBuildArtifact Artifact { get; init; }

    public required WinPeToolPaths Tools { get; init; }

    public required IReadOnlyList<string> DriverDirectories { get; init; }

    public required string WinPeLanguage { get; init; }
    public WinPeBootImageSource BootImageSource { get; init; } = WinPeBootImageSource.WinPe;
    public IProgress<WinPeMountedImageCustomizationProgress>? Progress { get; init; }

    public string? FoundryConnectConfigurationJson { get; init; }
    public IReadOnlyList<FoundryConnectProvisionedAssetFile> FoundryConnectAssetFiles { get; init; } = Array.Empty<FoundryConnectProvisionedAssetFile>();
    public string? ExpertDeployConfigurationJson { get; init; }
    public IReadOnlyList<AutopilotProfileSettings> AutopilotProfiles { get; init; } = Array.Empty<AutopilotProfileSettings>();
}
