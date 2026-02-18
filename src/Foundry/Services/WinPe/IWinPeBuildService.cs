namespace Foundry.Services.WinPe;

public interface IWinPeBuildService
{
    Task<WinPeResult<WinPeBuildArtifact>> BuildAsync(
        WinPeBuildOptions options,
        CancellationToken cancellationToken = default);
}
