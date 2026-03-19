namespace Foundry.Services.WinPe;

internal interface IWinPeMountedImageAssetProvisioningService
{
    Task<WinPeResult> ProvisionAsync(
        string mountedImagePath,
        WinPeArchitecture architecture,
        string? expertDeployConfigurationJson,
        CancellationToken cancellationToken);
}
