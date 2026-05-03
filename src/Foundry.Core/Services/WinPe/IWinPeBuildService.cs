namespace Foundry.Core.Services.WinPe;

public interface IWinPeBuildService
{
    Task<WinPeResult<WinPeBuildArtifact>> BuildAsync(
        WinPeBuildOptions options,
        CancellationToken cancellationToken = default);
}
