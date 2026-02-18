namespace Foundry.Services.WinPe;

public interface IMediaOutputService
{
    Task<WinPeResult<IReadOnlyList<WinPeUsbDiskCandidate>>> GetUsbCandidatesAsync(
        CancellationToken cancellationToken = default);

    Task<WinPeResult> CreateIsoAsync(
        IsoOutputOptions options,
        CancellationToken cancellationToken = default);

    Task<WinPeResult> CreateUsbAsync(
        UsbOutputOptions options,
        CancellationToken cancellationToken = default);
}
