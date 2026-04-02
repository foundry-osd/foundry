namespace Foundry.Services.WinPe;

internal interface IWinPeLocalConnectEmbeddingService
{
    Task<WinPeResult> ProvisionAsync(
        string mountedImagePath,
        WinPeArchitecture architecture,
        string workingDirectoryPath,
        CancellationToken cancellationToken);
}
