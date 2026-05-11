namespace Foundry.Core.Services.WinPe;

public interface IWinPeRuntimePayloadProvisioningService
{
    Task<WinPeResult> ProvisionAsync(
        WinPeRuntimePayloadProvisioningOptions options,
        IProgress<WinPeDownloadProgress>? downloadProgress = null,
        CancellationToken cancellationToken = default);
}
