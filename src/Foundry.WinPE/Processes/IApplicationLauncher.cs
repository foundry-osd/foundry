// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.WinPE.Processes;

/// <summary>Launches the UI process; cancelling observation never terminates it.</summary>
internal interface IApplicationLauncher
{
    /// <summary>
    /// Runs <paramref name="executable"/> (the guardian's own Foundry.WinPE executable) with <c>--ui</c> as a new launch, waits
    /// for its advertised readiness within the startup deadline, then for its exit, and reads its UI state file; the UI process
    /// locates <c>foundry.winpe.config.json</c> itself. Each call is a new launch with its own launch id, status file and UI
    /// state file.
    /// </summary>
    Task<ApplicationLaunchResult> RunUiAsync(string executable, IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken);
}
