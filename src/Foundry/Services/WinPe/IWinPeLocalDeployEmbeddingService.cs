namespace Foundry.Services.WinPe;

internal interface IWinPeLocalDeployEmbeddingService
{
    Task<WinPeResult> ProvisionAsync(
        string mountedImagePath,
        WinPeArchitecture architecture,
        string workingDirectoryPath,
        CancellationToken cancellationToken);
}
