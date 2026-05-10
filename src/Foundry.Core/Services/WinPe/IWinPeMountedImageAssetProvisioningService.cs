namespace Foundry.Core.Services.WinPe;

public interface IWinPeMountedImageAssetProvisioningService
{
    Task<WinPeResult> ProvisionAsync(
        WinPeMountedImageAssetProvisioningOptions options,
        CancellationToken cancellationToken = default);
}
