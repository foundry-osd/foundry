namespace Foundry.Core.Services.WinPe.OsRecovery;

public interface IOsRecoveryPayloadProvisioningService
{
    Task<WinPeResult<OsRecoveryPayloadProvisioningResult>> ProvisionAsync(
        OsRecoveryPayloadProvisioningOptions options,
        IProgress<WinPeDownloadProgress>? downloadProgress = null,
        CancellationToken cancellationToken = default);
}
