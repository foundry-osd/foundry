// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Creates a WinPE workspace from the Windows ADK.
/// </summary>
public interface IWinPeBuildService
{
    /// <summary>
    /// Builds a WinPE workspace and returns the paths needed by later media stages.
    /// </summary>
    /// <param name="options">The WinPE build options.</param>
    /// <param name="cancellationToken">A token used to cancel ADK execution.</param>
    /// <returns>The build artifact or a diagnostic failure.</returns>
    Task<WinPeResult<WinPeBuildArtifact>> BuildAsync(
        WinPeBuildOptions options,
        CancellationToken cancellationToken = default);
}
