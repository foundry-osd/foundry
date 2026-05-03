namespace Foundry.Core.Services.WinPe;

public interface IWinPeRuntimePayloadProvisioningService
{
    Task<WinPeResult> ProvisionAsync(
        WinPeRuntimePayloadProvisioningOptions options,
        CancellationToken cancellationToken = default);
}
