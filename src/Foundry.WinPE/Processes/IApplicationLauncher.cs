// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.WinPE.Ui;

namespace Foundry.WinPE.Processes;

/// <summary>Launches the UI process; cancelling observation never terminates it.</summary>
internal interface IApplicationLauncher
{
    /// <summary>
    /// Runs <paramref name="executable"/> (the guardian's own Foundry.WinPE executable) with <c>--ui</c> as a new launch, waits
    /// for its advertised readiness within the startup deadline, then supervises it until it exits and reads its UI state file;
    /// the UI process locates <c>foundry.winpe.config.json</c> itself. Each call is a new launch with its own launch id, status
    /// file and UI state file.
    /// </summary>
    /// <param name="executable">The Foundry.WinPE executable.</param>
    /// <param name="environment">The variables to set (or remove, when <see langword="null"/>) for the UI process.</param>
    /// <param name="onUiState">
    /// Receives the UI state about once a second while the ready UI process runs, on a thread-pool thread; it must not throw.
    /// </param>
    /// <param name="cancellationToken">Stops observing; the UI process keeps running.</param>
    Task<ApplicationLaunchResult> RunUiAsync(string executable, IReadOnlyDictionary<string, string?> environment,
        Action<UiState>? onUiState, CancellationToken cancellationToken);
}
