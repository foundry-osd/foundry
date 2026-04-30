namespace Foundry.Services.WinPe;

internal interface IWinPeLocalConnectEmbeddingService
{
    Task<WinPeResult> ProvisionAsync(
        string mountedImagePath,
        string mediaDirectoryPath,
        WinPeArchitecture architecture,
        string workingDirectoryPath,
        CancellationToken cancellationToken);
}
