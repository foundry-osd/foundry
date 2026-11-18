// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.WinPE.Processes;

/// <summary>Launches the UI process once; cancelling observation never terminates it.</summary>
internal interface IApplicationLauncher
{
    /// <summary>
    /// Runs <paramref name="executable"/> (the guardian's own Foundry.WinPE executable) with <c>--ui</c>, waits for its
    /// advertised readiness within the startup deadline, then for its exit; the UI process locates
    /// <c>foundry.winpe.config.json</c> itself.
    /// </summary>
    Task<ApplicationLaunchResult> RunUiAsync(string executable, IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken);
}
