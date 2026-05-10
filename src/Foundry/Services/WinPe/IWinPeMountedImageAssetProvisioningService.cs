using Foundry.Models.Configuration;

namespace Foundry.Services.WinPe;

internal interface IWinPeMountedImageAssetProvisioningService
{
    Task<WinPeResult> ProvisionAsync(
        string mountedImagePath,
        WinPeArchitecture architecture,
        string? foundryConnectConfigurationJson,
        IReadOnlyList<FoundryConnectProvisionedAssetFile> foundryConnectAssetFiles,
        string? expertDeployConfigurationJson,
        IReadOnlyList<AutopilotProfileSettings> autopilotProfiles,
        CancellationToken cancellationToken);
}
