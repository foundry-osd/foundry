namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Prepares a WinPE workspace with drivers, language packs, runtime payloads, and staged media assets.
/// </summary>
public interface IWinPeWorkspacePreparationService
{
    /// <summary>
    /// Runs the requested workspace preparation flow.
    /// </summary>
    /// <param name="options">Workspace preparation inputs and progress sinks.</param>
    /// <param name="cancellationToken">Token that cancels preparation.</param>
    /// <returns>The workspace preparation result, including staged artifact paths when successful.</returns>
    Task<WinPeResult<WinPeWorkspacePreparationResult>> PrepareAsync(
        WinPeWorkspacePreparationOptions options,
        CancellationToken cancellationToken = default);
}
