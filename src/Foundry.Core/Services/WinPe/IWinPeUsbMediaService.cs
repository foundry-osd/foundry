namespace Foundry.Core.Services.WinPe;

public interface IWinPeUsbMediaService
{
    Task<WinPeResult<IReadOnlyList<WinPeUsbDiskCandidate>>> GetUsbCandidatesAsync(
        WinPeToolPaths tools,
        string workingDirectoryPath,
        CancellationToken cancellationToken = default);

    Task<WinPeResult<WinPeUsbProvisionResult>> ProvisionAndPopulateAsync(
        UsbOutputOptions options,
        WinPeBuildArtifact artifact,
        WinPeToolPaths tools,
        bool useBootEx,
        CancellationToken cancellationToken = default);
}
