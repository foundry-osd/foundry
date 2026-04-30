namespace Foundry.Services.WinPe;

public interface IMediaOutputService
{
    WinPeResult<IReadOnlyList<string>> GetAvailableWinPeLanguages(
        WinPeArchitecture architecture = WinPeArchitecture.X64,
        string? adkRootPath = null);

    Task<WinPeResult<IReadOnlyList<WinPeUsbDiskCandidate>>> GetUsbCandidatesAsync(
        CancellationToken cancellationToken = default);

    Task<WinPeResult> CreateIsoAsync(
        IsoOutputOptions options,
        CancellationToken cancellationToken = default);

    Task<WinPeResult> CreateUsbAsync(
        UsbOutputOptions options,
        CancellationToken cancellationToken = default);
}
